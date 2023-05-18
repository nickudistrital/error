using Abrantix.MDB2Serial.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace Abrantix.MDB2Serial.MDBConverter
{
    public class MDBStreamDriver
    {
        private class Message
        {
            public CPControlCode control { get; set; }
            public byte[] data { get; set; }
        }

        private Stream mdbStream;
        private bool slaveMode;
        private Queue<Message> slaveMessageQueue;

        public event MessageTraceEventHandler MessageTrace;
        public event MessageTraceEventHandler DetailMessageTrace;

        public bool ShutdownExpected { get; set; }

        public int MaxRetries { get; set; }

        public MDBStreamDriver(Stream mdbStream, int maxRetries)
            : this(mdbStream, maxRetries, false)
        { }

        public MDBStreamDriver(Stream mdbStream, int maxRetries, bool slaveMode)
        {
            this.mdbStream = mdbStream;
            this.MaxRetries = maxRetries;
            this.slaveMode = slaveMode;
            this.slaveMessageQueue = new Queue<Message>(1);
        }

        public void Shutdown()
        {
        }

        /// <summary>
        /// Sends the STX..DLE ETX Message to the Serial Port and waits for ACK and payload.
        /// </summary>
        /// <param name="control">The CP command to be executed.</param>
        /// <param name="data">The payload of the command to be executed.</param>
        /// <param name="cpControlCode">The CP response of the tiny.</param>
        /// <returns>The payload of the CP response of the tiny.</returns>
        public byte[] SendMessageAwaitCPAckAndPayload(CPControlCode control, byte[] data, out CPControlCode cpControlCode)
        {
            int retriesLeft = this.MaxRetries;
            while (!ShutdownExpected && ((retriesLeft > 0) || (retriesLeft < 0)))
            {
                SendMessageAwaitCPAck(control, data);

                byte[] result = ReadMessage(out cpControlCode);
                if (result != null)
                {
                    return result;
                }
                retriesLeft--;
                Thread.Sleep(200);
            }
            if (ShutdownExpected)
            {
                throw new WaitException("Shutdown while waiting for CP ACK after send.");
            }
            throw new WaitException("Max retries reached while waiting for CP ACK after send.");
        }

        /// <summary>
        /// Sends the STX..DLE ETX Message to the Serial Port and waits for ACK
        /// </summary>
        public byte[] SendMessageAwaitCPAckAndPayload(CPControlCode control, byte[] data)
        {
            CPControlCode controlCode;
            return SendMessageAwaitCPAckAndPayload(control, data, out controlCode);
        }

        public void SendMessageAwaitCPAck(CPControlCode control, byte[] data, bool writeDirect = false)
        {
            WriteMessage(control, data, writeDirect);
            byte i = (byte)mdbStream.ReadByte();
            if ((CPCode)i != CPCode.ACK)
            {
                throw new FormatException(string.Format("Unexpected CPCode received. Expected ACK, reveived '0x{0:X}'", i));
            }
        }

        /// <summary>
        /// Sends a message and waits for the ACK, and then continues to Poll until the expected answer is received or the <see cref="MaxRetries"/> are exceeded.
        /// </summary>
        public bool SendWithCPAckAndWaitForExpected(CPControlCode control, byte[] data, MDBReaderCommand expectedAnswer, out byte[] answer)
        {
            byte[] firstAnswer = SendMessageAwaitCPAckAndPayload(control, data);
            return WaitForExpectedAnswer(expectedAnswer, out answer, firstAnswer);
        }

        /// <summary>
        /// Sends the STX..DLE ETX Message to the Serial Port
        /// </summary>
        /// <param name="data"></param>
        public void WriteMessage(CPControlCode control, byte[] data, bool writeDirect)
        {
            Message message = new Message();
            message.control = control;
            message.data = data;

            if (slaveMode && !writeDirect)
            {
                this.slaveMessageQueue.Enqueue(message);
            }
            else
            {
                if (writeDirect)
                {
                    //ACK potential pending request
                    try
                    {
                        mdbStream.ReadExisting();
                    }
                    catch { };
                    mdbStream.Write(new byte[] { (byte)CPCode.ACK }, 0, 1);
                }
                WriteMessage(message);
            }
        }

        private void WriteMessage(Message message)
        {
            byte[] cpMessage = BuildMessage(message.control, message.data);

            string mdbTraceMessage = "<no data>";
            if (message.data != null)
            {
                string mdbCommandString;
                if (slaveMode)
                {
                    mdbCommandString = message.data.GetMDBReaderCommandName();
                    mdbTraceMessage = message.data.GetMDBReaderTraceMessage();
                }
                else
                {
                    mdbCommandString = message.data.GetMDBMasterCommandName();
                    mdbTraceMessage = message.data.GetMDBMasterTraceMessage();
                }
                TraceMainMDBMessage(mdbCommandString, false);
            }
            OnDetailMessageTrace(string.Format("MDB:\t<- {0}", mdbTraceMessage));
            OnDetailMessageTrace(string.Format("CP:\t<- {0}", cpMessage.ToHexString()));

            mdbStream.Write(cpMessage, 0, cpMessage.Length);
            mdbStream.Flush();

            Thread.Sleep(50);
        }

        public byte[] ReadMessage()
        {
            CPControlCode controlCode;
            return ReadMessage(out controlCode);
        }

        /// <summary>
        /// Reads a STX..DLE ETX message from Bus
        /// </summary>
        /// <returns></returns>
        public byte[] ReadMessage(out CPControlCode controlCode)
        {
            List<byte> payload = new List<byte>();
            List<byte> cpMessage = new List<byte>();
            string cpTrace = string.Empty;
            CPCode state = CPCode.ETX;
            bool cpControlChecked = false;
            bool messageReceived = false;
            bool nak = false;

            controlCode = (CPControlCode)0xFF;

            try
            {
                bool ackAndFlush = false;
                while (!messageReceived)
                {
                    byte i = (byte)mdbStream.ReadByte();
                    cpMessage.Add(i);

                    switch (state)
                    {
                        case CPCode.STX:
                            if (i == (byte)CPCode.DLE)
                            {
                                state = CPCode.DLE;
                            }
                            else
                            {
                                // The first byte of the payload is the CP control code
                                if (!cpControlChecked)
                                {
                                    controlCode = (CPControlCode)i;
                                    if ((byte)CPControlCode.DATA != i &&
                                        (byte)CPControlCode.STATUSINFO != i &&
                                        (byte)CPControlCode.GSM_STATUSINFO != i &&
                                        (byte)CPControlCode.MDB_RESET != i)
                                    {
                                        string error = string.Format("Unexpected CPControlCode '0x{0:X}'", i);
                                        throw new FormatException(error);
                                    }
                                    cpControlChecked = true;
                                }
                                else
                                {
                                    payload.Add(i);
                                }
                            }
                            break;
                        case CPCode.DLE:
                            switch (i)
                            {
                                case (byte)CPCode.DLE:
                                    //escaped DLE
                                    payload.Add(i);
                                    state = CPCode.STX;
                                    break;
                                case (byte)CPCode.ETX:
                                    state = CPCode.ETX;
                                    messageReceived = true;

                                    ackAndFlush = true;

                                    break;
                                default:
                                    //unknown escape command               
                                    break;
                            }
                            break;
                        case CPCode.ETX:
                            switch ((CPCode)i)
                            {
                                case CPCode.STX:
                                    state = CPCode.STX;
                                    break;
                                case CPCode.ACK:
                                    cpTrace = "CP ACK";
                                    break;
                                case CPCode.NAK:
                                    cpTrace = "CP NAK";
                                    nak = true;
                                    messageReceived = true;
                                    break;
                                default:
                                    break;
                            }
                            break;
                        default:
                            break;
                    }
                }

                OnDetailMessageTrace(string.Format("CP:\t-> {0} ({1})", cpMessage.ToArray().ToHexString(), cpTrace));
                byte[] payloadBytes = payload.ToArray();

                string mdbCommandString;
                string traceMessage;
                if (slaveMode)
                {
                    mdbCommandString = payloadBytes.GetMDBMasterCommandName();
                    traceMessage = payloadBytes.GetMDBMasterTraceMessage();
                }
                else
                {
                    mdbCommandString = payloadBytes.GetMDBReaderCommandName();
                    traceMessage = payloadBytes.GetMDBReaderTraceMessage();
                }

                TraceMainMDBMessage(mdbCommandString, true);
                OnDetailMessageTrace(string.Format("MDB:\t-> {0}", traceMessage));

                if (ackAndFlush)
                {
                    // Send a CP Ack to acknowledge the reception of the message on CP level
                    mdbStream.Write(new byte[] { (byte)CPCode.ACK }, 0, 1);

                    bool isResetCommand = false;
                    if ((payloadBytes != null) && (payloadBytes.Length > 0))
                    {
                        if ((MDBMasterCommand)payloadBytes[0] == MDBMasterCommand.RESET)
                        {
                            isResetCommand = true;
                        }
                    }

                    //don't send queued command back when received command is a "RESET"
                    if (!isResetCommand && slaveMode && this.slaveMessageQueue.Count > 0)
                    {
                        Message message = this.slaveMessageQueue.Dequeue();
                        WriteMessage(message);
                    }
                }

                if (CPControlCode.MDB_RESET == controlCode)
                {
                    OnMessageTrace("Received MDB Bus Reset.");
                    throw new MDBResetException();
                }

                if (nak)
                {
                    return null;
                }
                else
                {
                    return payload.ToArray();
                }
            }
            catch (TimeoutException)
            {
                if (state != CPCode.ETX)
                {
                    OnMessageTrace(string.Format("Read Timeout: {0}", payload.ToArray().ToHexString()));
                }
                return null;
            }
        }

        /// <summary>
        /// This will Reset the serial driver on the application level.
        /// The serial port will stay open, but e.g. in slave mode, 
        /// all outstanding commands that should be written to the master will be purged.
        /// </summary>
        public void EnqueueMDBCommand(CPControlCode control, byte[] data)
        {
            ClearSlaveMessageQueue();
            this.slaveMessageQueue.Enqueue(new Message()
            {
                control = control,
                data = data
            });
        }

        /// <summary>
        /// Clears the slave message queue
        /// </summary>
        public void ClearSlaveMessageQueue()
        {
            this.slaveMessageQueue.Clear();
        }

        /// <summary>
        /// Sends the given MDB Poll Command and waits for the Message answer
        /// </summary>
        private bool WaitForExpectedAnswer(MDBReaderCommand expectedCommand, out byte[] answer)
        {
            return WaitForExpectedAnswer(expectedCommand, out answer, null);
        }

        private bool WaitForExpectedAnswer(MDBReaderCommand expectedCommand, out byte[] answer, byte[] alreadyReceivedData)
        {
            answer = alreadyReceivedData;

            if (alreadyReceivedData != null)
            {
                if (IsExpected(expectedCommand, ref alreadyReceivedData))
                {
                    return true;
                }
            }

            answer = null;
            int retriesLeft = this.MaxRetries;
            while (!ShutdownExpected && ((retriesLeft > 0) || (retriesLeft < 0)))
            {
                WriteMessage(CPControlCode.DATA, Constants.ReaderPoll, false);
                answer = ReadMessage();

                if ((answer != null) && (answer.Length > 1))
                {
                    if (IsExpected(expectedCommand, ref answer))
                    {
                        return true;
                    }
                    else
                    {
                        // Some payload, but not ACK and not the expected
                        return false;
                    }
                }

                retriesLeft--;

                if (!this.slaveMode)
                {
                    // This is a test implementation. In real life this should be done non-blocking.
                    // Also, 100ms is rather long
                    Thread.Sleep(100);
                }
            }
            return false;
        }

        private bool IsExpected(MDBReaderCommand expectedCommand, ref byte[] data)
        {
            bool dataIsTheExpected = false;
            if (data != null)
            {
                if (data.Length > 1)
                {
                    if (data[0] == (byte)expectedCommand)
                    {
                        dataIsTheExpected = true;
                    }
                    else
                    {
                        OnMessageTrace(string.Format("Expected {0} but got {1}", expectedCommand, data.GetMDBReaderCommandName()));
                    }
                }
            }
            return dataIsTheExpected;
        }

        /// <summary>
        /// Builds a STX..DLE ETX Message from given data
        /// </summary>
        /// <param name="control"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public static byte[] BuildMessage(CPControlCode control, byte[] data)
        {
            List<byte> byteList = new List<byte>(100);
            byteList.Add((byte)CPCode.STX);

            if ((byte)control == (byte)CPCode.DLE)
            {
                //escape DLE
                byteList.Add((byte)CPCode.DLE);
            }

            byteList.Add((byte)control);

            if (data != null)
            {
                foreach (byte b in data)
                {
                    if (b == (byte)CPCode.DLE)
                    {
                        //escape DLE
                        byteList.Add((byte)CPCode.DLE);
                    }
                    byteList.Add(b);
                }
            }

            byteList.Add((byte)CPCode.DLE);
            byteList.Add((byte)CPCode.ETX);

            return byteList.ToArray();
        }

        private void TraceMainMDBMessage(string message, bool incoming)
        {
            if ((message != "ACK") && (message != "<none>") && (message != "POLL"))
            {
                OnMessageTrace(string.Format("MDB:\t{0} {1}", incoming ? "->" : "<-", message));
            }
        }

        private void OnDetailMessageTrace(string message)
        {
            if (this.DetailMessageTrace != null)
            {
                MessageTraceEventArgs args = new MessageTraceEventArgs() { Message = message };
                this.DetailMessageTrace(this, args);
            }
        }

        private void OnMessageTrace(string message)
        {
            if (this.MessageTrace != null)
            {
                MessageTraceEventArgs args = new MessageTraceEventArgs() { Message = message };
                this.MessageTrace(this, args);
            }
        }
    }
}
