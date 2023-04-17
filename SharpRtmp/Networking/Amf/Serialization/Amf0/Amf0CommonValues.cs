namespace SharpRtmp.Networking.Amf.Serialization.Amf0;

public static class Amf0CommonValues
{
    public const int TimezoneLength = 2;
    public const int MarkerLength = 1;
    public const int StringHeaderLength = sizeof(ushort);
    public const int LongStringHeaderLength = sizeof(uint);
}