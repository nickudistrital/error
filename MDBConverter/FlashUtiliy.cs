using Abrantix.MDB2Serial.Common;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace Abrantix.MDB2Serial.MDBConverter
{
    public class FlashUtiliy
    {
        public delegate void MessageTraceEventHandler(object sender, MessageTraceEventArgs args);

        public class MessageTraceEventArgs : EventArgs
        {
            public string Message { get; set; }
        }

        /// <summary>
        /// Consume this event if you want to listen to trace messages of this class.
        /// </summary>
        public event MessageTraceEventHandler MessageTrace;

        Stream mdbStream = null;
        public bool IsStopRequested { get; set; }
        private bool isMaster;
        private string hexFileName;

        public FlashUtiliy(Stream mdbStream, string hexFileName, bool isMaster)
        {
            this.mdbStream = mdbStream;
            this.isMaster = isMaster;
            this.hexFileName = hexFileName;
        }

        public void Flash()
        {
            try
            {
                OnMessageTrace(string.Format("Start Main flash operation...{0}", Environment.NewLine));
                if (!File.Exists(hexFileName))
                {
                    throw new ArgumentException(string.Format("Flash File {0} not found!", hexFileName));
                }

                FlashBootloader();

                try
                {
                    mdbStream.ReadExisting();
                }
                catch { };

                Thread.Sleep(3000);

                //ACK potential pending request
                mdbStream.Write(new byte[] { (byte)CPCode.ACK }, 0, 1);
                try
                {
                    mdbStream.ReadExisting();
                }
                catch { };

                byte[] loaderPreamble = new byte[] { 0x59, 0xDF, 0x69, 0x5A, 0x20, 0x1A, 0xBD, 0x1C };
                byte[] enterBootLoaderCommand = MDBStreamDriver.BuildMessage(CPControlCode.FLASH_UPDATE, loaderPreamble);
                OnMessageTrace(string.Format("<- {0}", enterBootLoaderCommand.ToHexString()));
                mdbStream.Write(enterBootLoaderCommand, 0, enterBootLoaderCommand.Length);
                ReadCPAck(mdbStream, "Could not enter boot loader");


                using (StreamReader streamReader = File.OpenText(hexFileName))
                {
                    string line;

                    while (!this.IsStopRequested && ((line = streamReader.ReadLine()) != null))
                    {
                        if (line.Trim() != string.Empty)
                        {
                            if (line.StartsWith(":"))
                            {
                                int result = sendBlock(mdbStream, line.Trim());
                                if (result == (byte)CPCode.EOT)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }

                if (isMaster)
                {
                    OnMessageTrace(string.Format("Setting MDB Master Mode...{0}", Environment.NewLine));
                    Thread.Sleep(6000);
                    byte[] setMDBMasterModeCommand = MDBStreamDriver.BuildMessage(CPControlCode.MDB_MODE, new byte[] { 0x01 });
                    OnMessageTrace(string.Format("<- {0}", setMDBMasterModeCommand.ToHexString()));
                    mdbStream.Write(setMDBMasterModeCommand, 0, setMDBMasterModeCommand.Length);
                    ReadCPAck(mdbStream, "No ACK for MDB CP Master Mode");
                    OnMessageTrace(string.Format("MDB Master Mode set.{0}", Environment.NewLine));
                }
                else
                {
                    OnMessageTrace(string.Format("Device has been flashed as slave. MDB Slave Mode does not have to be set.{0}", Environment.NewLine));
                }

                OnMessageTrace(string.Format("Flash operation finished.{0}", Environment.NewLine));
            }
            catch (Exception ex)
            {
                OnMessageTrace(string.Format("Flash operation failed:{0}{1}", Environment.NewLine, ex.ToString()));
            }
            finally
            {
            }
        }

        /// <summary>
        /// Flash bootloader if necessary
        /// Due to bootloader incompatibilities, Bootloader update must be done when updating from <V2.28
        /// </summary>
        private void FlashBootloader()
        {
            try
            {
                //Get FW Version
                CPControlCode cpControlCode;
                byte[] statusRequestCommand = MDBStreamDriver.BuildMessage(CPControlCode.STATUSREQUEST, null);
                OnMessageTrace(string.Format("<- {0}", statusRequestCommand.ToHexString()));
                mdbStream.Write(statusRequestCommand, 0, statusRequestCommand.Length);

                byte[] cpMessage = ReadMessage(out cpControlCode);

                if (cpControlCode != CPControlCode.STATUSINFO || cpMessage.Length < 2)
                {
                    throw new FormatException(string.Format("Unexpected StatusInfo '{0}'", cpControlCode));
                }

                OnMessageTrace(string.Format("Bootloader V{1:X02}.{2:X02} found...{0}", Environment.NewLine, cpMessage[0], cpMessage[1]));

                //update Bootloader only if < 2.28
                if (cpMessage[0] > 0x02 || (cpMessage[0] == 0x02 && cpMessage[1] >= 0x28))
                {
                    OnMessageTrace(string.Format("Bootloader flash not necessary...{0}", Environment.NewLine));
                    return;
                }

                OnMessageTrace(string.Format("Start bootloader flash operation...{0}", Environment.NewLine));

                FileInfo fi = new FileInfo(hexFileName);
                string bootloaderFile = Path.Combine(fi.DirectoryName, "Bootloader.hex");

                if (!File.Exists(bootloaderFile))
                {
                    throw new FormatException(string.Format("Bootloader Flash File {0} not found!", bootloaderFile));
                }

                Thread.Sleep(100);
                try
                {
                    mdbStream.ReadExisting();
                }
                catch { };

                Thread.Sleep(3000);

                //ACK potential pending request
                mdbStream.Write(new byte[] { (byte)CPCode.ACK }, 0, 1);
                try
                {
                    mdbStream.ReadExisting();
                }
                catch { };

                byte[] loaderPreamble = new byte[] { 0x59, 0xDF, 0x69, 0x5A, 0x20, 0x1A, 0xBD, 0x1C };
                byte[] enterBootLoaderCommand = MDBStreamDriver.BuildMessage(CPControlCode.FLASH_UPDATE, loaderPreamble);
                OnMessageTrace(string.Format("<- {0}", enterBootLoaderCommand.ToHexString()));
                mdbStream.Write(enterBootLoaderCommand, 0, enterBootLoaderCommand.Length);
                ReadCPAck(mdbStream, "Could not enter boot loader");

                using (StreamReader streamReader = File.OpenText(bootloaderFile))
                {
                    string line;

                    while (!this.IsStopRequested && ((line = streamReader.ReadLine()) != null))
                    {
                        if (line.Trim() != string.Empty)
                        {

                            if (line.StartsWith(":"))
                            {
                                int result = sendBlock(mdbStream, line.Trim());
                                if (result == (byte)CPCode.EOT)
                                {
                                    break;
                                }
                            }
                        }
                    }
                }

                //Wait for device restart
                Thread.Sleep(4000);

                OnMessageTrace(string.Format("Bootloader Flash operation finished.{0}", Environment.NewLine));
            }
            finally
            {
            }
        }

        private void ReadCPAck(Stream stream, string errorMessage)
        {
            int i = 0;
            try
            {
                i = stream.ReadByte();
            }
            catch (Exception ex)
            {
                OnMessageTrace(string.Format("Failed to read from serial port: {0}{1}{0}", Environment.NewLine, ex.ToString()));
            }

            if ((byte)i != (byte)CPCode.ACK)
            {
                throw new FormatException(string.Format("{0}: {1:x2}", errorMessage, i));
            }
        }

        private int sendBlock(Stream mdbStream, string s)
        {
            StringBuilder sendStringBuilder = new StringBuilder();
            sendStringBuilder.Append(" <- ");
            int i;
            do
            {
                sendStringBuilder.Append(string.Format("{0:X2}", (byte)CPCode.STX));
                mdbStream.Write(new byte[] { (byte)CPCode.STX }, 0, 1);
                foreach (byte c in s.ToCharArray())
                {
                    if (c == (byte)CPCode.DLE)
                    {
                        sendStringBuilder.Append(string.Format("{0:X2}", (byte)CPCode.DLE));
                        mdbStream.Write(new byte[] { (byte)CPCode.DLE }, 0, 1);
                    }
                    sendStringBuilder.Append(string.Format("{0:X2}", c));
                    mdbStream.Write(new byte[] { c }, 0, 1);
                }
                sendStringBuilder.Append(string.Format("{0:X2}{1:X2}", (byte)CPCode.DLE, (byte)CPCode.ETX));
                mdbStream.Write(new byte[] { (byte)CPCode.DLE, (byte)CPCode.ETX }, 0, 2);
                i = mdbStream.ReadByte();
                OnMessageTrace(sendStringBuilder.ToString());
                OnMessageTrace(string.Format(" -> {0:X2} ", i));
            } while (i != (byte)CPCode.ACK && i != (byte)CPCode.EOT);
            return i;
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

            controlCode = (CPControlCode)0xFF;

            try
            {
                //bool ackAndFlush = false;
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

                                    //ackAndFlush = true;

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

                byte[] payloadBytes = payload.ToArray();

                if (CPControlCode.MDB_RESET == controlCode)
                {
                    OnMessageTrace("Received MDB Bus Reset.");
                    throw new MDBResetException();
                }

                return payload.ToArray();
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
