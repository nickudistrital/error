using System;

namespace Abrantix.MDB2Serial.Common
{
    public delegate void MessageTraceEventHandler(object sender, MessageTraceEventArgs args);

    public class MessageTraceEventArgs : EventArgs
    {
        public string Message { get; set; }
    }
}
