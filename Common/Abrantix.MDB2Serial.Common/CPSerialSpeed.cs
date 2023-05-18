namespace Abrantix.MDB2Serial.Common
{

    /**
     *  Serial Speed enumeration - used in conjuction with CPControlCode SET_SERIAL_SPEED
     */

    public enum CPSerialSpeed : byte
    {
        SPEED_115200 = 0,
        SPEED_57600 = 1,
        SPEED_38400 = 2,
        SPEED_19200 = 3,
        SPEED_9600 = 4,
    }
}
