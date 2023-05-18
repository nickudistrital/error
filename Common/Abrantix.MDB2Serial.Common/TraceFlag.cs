using System;

namespace Abrantix.MDB2Serial.Common
{
    [Flags]
    public enum MDBTraceFlag
    {
        /// <summary>
        /// Silent.
        /// </summary>
        None = 0,

        /// <summary>
        /// Main MDB messages such as e.g. VEND SUCCESS.
        /// </summary>
        MDB = 1,

        /// <summary>
        /// Everything on the MDB bus (MDB and CP IO).
        /// Take care when using this settings.
        /// When running on weak processor, this can significantly slow down processing and cause weird MBD problems
        /// like "losing" messages.
        /// </summary>
        MDBDetail = 2,

        /// <summary>
        /// Non-MDB specific messages of the simulator.
        /// </summary>
        HighLevel = 4,

        /// <summary>
        /// Very detailed trace data.
        /// </summary>
        StateMachine = 8,
    }
}
