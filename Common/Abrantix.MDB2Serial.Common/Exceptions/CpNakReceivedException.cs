using System;

namespace Abrantix.MDB2Serial.Common
{
    public class CpNakReceivedException : Exception
    {
        public CpNakReceivedException(string message) : base(message)
        { }
    }
}
