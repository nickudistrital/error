namespace Abrantix.MDB2Serial.Common
{
    public enum CPCode : byte
    {
        STX = 0x02,
        ETX = 0x03,
        EOT = 0x04,
        DLE = 0x10,
        ACK = 0x06,
        NAK = 0x15
    }
}
