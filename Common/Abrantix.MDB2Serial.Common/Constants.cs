namespace Abrantix.MDB2Serial.Common
{
    public static class Constants
    {
        public static byte[] ReaderReset = new byte[] { (byte)MDBMasterCommand.RESET };
        public static byte[] ReaderSetupConfig = new byte[] { (byte)MDBMasterCommand.SETUP, 0x00, 0x03, 0x01, 0x01, 0x00 };
        public static byte[] ReaderSetupPrices = new byte[] { (byte)MDBMasterCommand.SETUP, 0x01, 0xFF, 0xFF, 0x00, 0x00 };
        public static byte[] ReaderPoll = new byte[] { (byte)MDBMasterCommand.POLL };
        public static byte[] ReaderVendRequest1 = new byte[] { (byte)MDBMasterCommand.VEND, 0x00, 0x00, 0x46, 0x00, 0x12 }; // 1.00, Item 1
        public static byte[] ReaderVendRequest2 = new byte[] { (byte)MDBMasterCommand.VEND, 0x00, 0x09, 0xc4, 0x00, 0x02 }; // 25.00, Item 2
        public static byte[] ReaderVendRequest3 = new byte[] { (byte)MDBMasterCommand.VEND, 0x00, 0x0C, 0xfd, 0x00, 0x03 }; // 33.25, Item 3
        public static byte[] ReaderVendRequest16 = new byte[] { (byte)MDBMasterCommand.VEND, 0x00, 0x00, 0x32, 0x00, 0x10 }; // 0.50, Item 16
        public static byte[] ReaderVendCancel = new byte[] { (byte)MDBMasterCommand.VEND, 0x01, 0xff, 0xff };
        public static byte[] ReaderVendSuccess = new byte[] { (byte)MDBMasterCommand.VEND, 0x02, 0xff, 0xff };
        public static byte[] ReaderVendFailure = new byte[] { (byte)MDBMasterCommand.VEND, 0x03 };
        public static byte[] ReaderVendSessionComplete = new byte[] { (byte)MDBMasterCommand.VEND, 0x04 };
        public static byte[] ReaderEnable = new byte[] { (byte)MDBMasterCommand.READER, 0x01 };
        public static byte[] ReaderDisable = new byte[] { (byte)MDBMasterCommand.READER, 0x00 };
        public static byte[] ReaderCancel = new byte[] { (byte)MDBMasterCommand.READER, 0x02 };
        public static byte[] ReaderRevalueLimit = new byte[] { (byte)MDBMasterCommand.REVALUE, 0x01 };
        public static byte[] ExpansionRequestID = new byte[] { (byte)MDBMasterCommand.EXPANSION, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
    }
}
