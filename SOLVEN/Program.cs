using System.IO.Ports;
using System.Threading;
using Abrantix.MDB2Serial.Common;
using Abrantix.MDB2Serial.MDBConverter;
using EmailSender;
using POS;
using SOLVEN.Common;

namespace SOLVEN
{
    class MainClass
    {
        /// <summary>
        /// The entry point of the program, where the program control starts and ends.
        /// </summary>
        /// <param name="args">The command-line arguments.</param>
        public static void Main(string[] args)
        {
            Trace("Starting SOLVEN v1.1.6 - Update Michael");
            // Port of the MDB2Serial (Adapter)
            string port = "/dev/serial0";
            Trace(string.Format("Trying to connect to {0}", port));
            // Create a new SerialPort object with default settings.
            var serialPort = SerialPortUtil.OpenSerialPort(port, 2000, 115200);
            var cashlessDevice = new CashlessDevice(serialPort.BaseStream, 0x10);
            // Run the cashless device
            cashlessDevice.Run();
        }

        private static void Trace(string message)
        {
            Logger.Log("Main:\t{0}", message);
        }
    }
}