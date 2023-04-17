using System;
using SharpRtmp.Networking.Rtmp.Data;
using SharpRtmp.Networking.Rtmp.Serialization;
using SharpRtmp.Networking.Utils;

namespace SharpRtmp.Networking.Rtmp.Messages;

public enum LimitType : byte
{
    Hard,
    Soft,
    Dynamic
}

[RtmpMessage(MessageType.SetPeerBandwidth)]
public class SetPeerBandwidthMessage : ControlMessage
{
    public uint WindowSize { get; set; }
    public LimitType LimitType { get; set; }

    public override void Deserialize(SerializationContext context)
    {
        WindowSize = NetworkBitConverter.ToUInt32(context.ReadBuffer.Span);
        LimitType = (LimitType)context.ReadBuffer.Span[sizeof(uint)..][0];
    }

    public override void Serialize(SerializationContext context)
    {
        var buffer = ArrayPool.Rent(sizeof(uint) + sizeof(byte));
        try
        {
            NetworkBitConverter.TryGetBytes(WindowSize, buffer);
            buffer.AsSpan(sizeof(uint))[0] = (byte)LimitType;
            context.WriteBuffer.WriteToBuffer(buffer.AsSpan(0, sizeof(uint) + sizeof(byte)));
        }
        finally
        {
            ArrayPool.Return(buffer);
        }
    }
}