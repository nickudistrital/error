using System;

namespace Abrantix.MDB2Serial.MDBConverter
{
    public class VendAbortedException : Exception
    {
        public byte MDBPollCommand
        {
            get;
            set;
        }

        public VendAbortedException(string message) : base(message) { }

        public VendAbortedException(string message, byte mdbPollCommand) : base(message)
        {
            this.MDBPollCommand = mdbPollCommand;
        }

        public VendAbortedException() : base() { }
    }
}
