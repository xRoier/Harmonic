using System;
using SharpRtmp.Networking.Rtmp.Data;
using SharpRtmp.Networking.Rtmp.Serialization;
using SharpRtmp.Networking.Utils;

namespace SharpRtmp.Networking.Rtmp.Messages;

[RtmpMessage(MessageType.SetChunkSize)]
public class SetChunkSizeMessage : ControlMessage
{
    public uint ChunkSize { get; set; }

    public override void Deserialize(SerializationContext context)
    {
        var chunkSize = NetworkBitConverter.ToInt32(context.ReadBuffer.Span);
        ChunkSize = (uint)chunkSize;
    }

    public override void Serialize(SerializationContext context)
    {
        var buffer = ArrayPool.Rent(sizeof(uint));
        try
        {
            NetworkBitConverter.TryGetBytes(ChunkSize, buffer);
            context.WriteBuffer.WriteToBuffer(buffer.AsSpan(0, sizeof(uint)));
        }
        finally
        {
            ArrayPool.Return(buffer);
        }
    }
}