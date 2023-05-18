namespace Abrantix.MDB2Serial.Common
{
    public enum CPControlCode : byte
    {
        DATA = 0x00,
        STATUSREQUEST = 0x10,
        STATUSINFO = 0x11,
        ADDRESSREGISTER = 0x12,
        ADDRESSUNREGISTER = 0x13,
        MDB_RESET = 0x14,
        FLASH_UPDATE = 0x15,
        GSM = 0x16,
        GSM_STATUSINFO = 0x17,
        LED = 0x18,
        MDB_MODE = 0x19,
        TESTMODE_FLAGS = 0x20,
        SET_SERIAL_SPEED = 0x21,
    }
}
