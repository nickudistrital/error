namespace Abrantix.MDB2Serial.Common
{
    public class MDBStatusInfo
    {
        private byte[] data;
        public MDBStatusInfo(byte[] data)
        {
            this.Data = data;
        }

        public string Version
        {
            get { return string.Format("{0:X}.{1:X}", data[0], data[1]); }
        }

        public MDBMode MDBMode
        {
            get { return (MDBMode)data[3]; }
        }

        public bool BusReady
        {
            get { return data[2] == 0x01 ? true : false; }
        }

        public byte[] Data
        {
            get { return data; }
            set
            {
                this.data = value;
            }
        }
    }
}
