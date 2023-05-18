using System;
using System.IO;
using System.Linq;

namespace Abrantix.MDB2Serial.Common
{
    public static class ExtensionMethods
    {
        public static char[] ReadExisting(this Stream stream)
        {
            char[] buffer = new char[65535];
            var sr = new StreamReader(stream);
            sr.Read(buffer, 0, buffer.Length);
            return buffer;
        }

        public static string ToHexString(this byte[] data)
        {
            string hex = BitConverter.ToString(data);
            hex = hex.Replace("-", " ");
            return hex;
        }

        public static byte[] ToByteArray(this string data)
        {
            return data
               .Split(' ')                               // Split into items 
               .Select(item => Convert.ToByte(item, 16)) // Convert each item into byte
               .ToArray();
        }

        public static string ToBcdString(this byte data)
        {
            return data.ToString("00");
        }

        public static string GetMDBReaderCommandName(this byte[] data)
        {
            string commandName = "<none>";

            if ((data != null) && (data.Length > 0))
            {
                MDBReaderCommand command = (MDBReaderCommand)data[0];
                commandName = Enum.GetName(typeof(MDBReaderCommand), command);

                if (command == MDBReaderCommand.JUST_RESET && data.Length < 2)
                {
                    // ACK and JUST_RESET are similar. ACK is 0x00, JUST_RESET is 0x00 0x00
                    commandName = "ACK";
                }
            }
            else
            {
                commandName = "ACK";
            }
            return commandName;
        }

        public static string GetMDBMasterCommandName(this byte[] data)
        {
            string commandName = "<none>";

            if ((data != null) && (data.Length > 0))
            {
                MDBMasterCommand command = (MDBMasterCommand)data[0];
                commandName = Enum.GetName(typeof(MDBMasterCommand), command);
                if (data.Length > 1)
                {
                    switch (command)
                    {
                        case MDBMasterCommand.VEND:
                            switch (data[1])
                            {
                                case 0x00:
                                    commandName = "VEND Request";
                                    break;
                                case 0x01:
                                    commandName = "VEND Cancel";
                                    break;
                                case 0x02:
                                    commandName = "VEND Success";
                                    break;
                                case 0x03:
                                    commandName = "VEND Failure";
                                    break;
                                case 0x04:
                                    commandName = "VEND Session Complete";
                                    break;
                            }
                            break;
                        case MDBMasterCommand.SETUP:
                            switch (data[1])
                            {
                                case 0x00:
                                    commandName = "SETUP Config Data";
                                    break;
                                case 0x01:
                                    commandName = "SETUP Max/Min Prices";
                                    break;
                            }
                            break;
                        case MDBMasterCommand.REVALUE:
                            switch (data[1])
                            {
                                case 0x00:
                                    commandName = "REVALUE Request";
                                    break;
                                case 0x01:
                                    commandName = "REVALUE Limit Request";
                                    break;
                            }
                            break;
                        case MDBMasterCommand.READER:
                            switch (data[1])
                            {
                                case 0x00:
                                    commandName = "READER Disable";
                                    break;
                                case 0x01:
                                    commandName = "READER Enable";
                                    break;
                                case 0x02:
                                    commandName = "READER Cancel";
                                    break;
                            }
                            break;
                    }
                }
            }

            return commandName;
        }

        public static string GetMDBMasterTraceMessage(this byte[] data)
        {
            string mdbMasterCommandString = data.GetMDBMasterCommandName();
            string mdbTraceMessage = string.Format("{0} ({1})", data.ToHexString(), mdbMasterCommandString);
            return mdbTraceMessage;
        }

        public static string GetMDBReaderTraceMessage(this byte[] data)
        {
            string mdbReaderCommandString = data.GetMDBReaderCommandName();
            string mdbTraceMessage = string.Format("{0} ({1})", data.ToHexString(), mdbReaderCommandString);
            return mdbTraceMessage;
        }
    }
}
