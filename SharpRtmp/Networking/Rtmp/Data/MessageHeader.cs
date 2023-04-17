using System;

namespace SharpRtmp.Networking.Rtmp.Data;

public class MessageHeader: ICloneable
{
    public uint Timestamp { get; set; }
    public uint MessageLength { get; internal set; }
    public MessageType MessageType { get; internal set; } = 0;
    public uint? MessageStreamId { get; internal set; }

    public object Clone() => MemberwiseClone();
}