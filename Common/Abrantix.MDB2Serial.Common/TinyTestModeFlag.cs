using System;

namespace Abrantix.MDB2Serial.Common
{
    [Flags]
    public enum TinyTestModeFlag : byte
    {
        None = 0,
        WeakACK = 1,
        IgnoreVendApproved = 2,
    }
}
