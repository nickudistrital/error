using System.IO.Ports;

namespace Abrantix.MDB2Serial.Common
{
    public static class SerialPortUtil
    {
        /// <summary>
        /// Open Serial Port
        /// </summary>
        public static SerialPort OpenSerialPort(string serialPortName, int readTimeout = 2000, int baudRate = 115200)
        {
            SerialPort serialPort = new SerialPort();
            serialPort.PortName = serialPortName;
            serialPort.BaudRate = baudRate;
            serialPort.DataBits = 8;
            serialPort.Parity = Parity.None;
            serialPort.StopBits = StopBits.One;
            serialPort.Handshake = Handshake.None;
            serialPort.ReadTimeout = readTimeout;
            serialPort.Open();
            return serialPort;
        }

        public static void CloseSerialPort(SerialPort serialPort)
        {
            if ((serialPort != null) && serialPort.IsOpen)
            {
                serialPort.DiscardInBuffer();
                serialPort.DiscardOutBuffer();
                serialPort.Close();
            }
        }
    }
}
