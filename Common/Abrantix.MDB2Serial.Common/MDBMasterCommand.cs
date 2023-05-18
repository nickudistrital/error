namespace Abrantix.MDB2Serial.Common
{
    public enum MDBMasterCommand : byte
    {
        RESET = 0x10,
        SETUP = 0x11,
        POLL = 0x12,
        VEND = 0x13,
        READER = 0x14,
        REVALUE = 0x15,
        EXPANSION = 0x17,

        //SOLVEN VMC
        CONSUME = 0x01,
        CANCEL = 0x02,
        RECHARGE = 0x03,
        QUERY = 0x04,
        DISPENSING_RESULT = 0x05
    }
}
