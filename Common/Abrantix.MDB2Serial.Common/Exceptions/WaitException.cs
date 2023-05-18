using System;

namespace Abrantix.MDB2Serial.Common
{
    public class WaitException : Exception
    {
        public WaitException()
        {
        }

        public WaitException(string message)
            : base(message)
        {
        }
    }
}
