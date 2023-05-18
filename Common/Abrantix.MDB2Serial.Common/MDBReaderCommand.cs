namespace Abrantix.MDB2Serial.Common
{
    public enum MDBReaderCommand : byte
    {
        JUST_RESET = 0x00,
        CONFIG_DATA = 0x01,
        DISPLAY_REQUEST = 0x02,
        BEGIN_SESSION = 0x03,
        SESSION_CANCEL_REQUEST = 0x04,
        VEND_APPROVED = 0x05,
        VEND_DENIED = 0x06,
        END_SESSION = 0x07,
        CANCELLED = 0x08,
        PERIPHERAL_ID = 0x09,
        OUT_OF_SEQUENCE = 0x0B,
        REVALUE_APPROVED = 0x0D,
        REVALUE_DENIED = 0x0E,
        REVALUE_LIMIT_AMOUNT = 0x0F,
    }
}
