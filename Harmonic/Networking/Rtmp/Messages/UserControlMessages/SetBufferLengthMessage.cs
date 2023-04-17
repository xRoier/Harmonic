﻿using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;
using System;
using System.Diagnostics.Contracts;

namespace Harmonic.Networking.Rtmp.Messages.UserControlMessages;

[UserControlMessage(Type = UserControlEventType.SetBufferLength)]
public class SetBufferLengthMessage : UserControlMessage
{
    public uint StreamId { get; set; }
    public uint BufferMilliseconds { get; set; }

    public override void Deserialize(SerializationContext context)
    {
        var span = context.ReadBuffer.Span;
        var eventType = (UserControlEventType)NetworkBitConverter.ToUInt16(span);
        span = span[sizeof(ushort)..];
        Contract.Assert(eventType == UserControlEventType.StreamIsRecorded);
        StreamId = NetworkBitConverter.ToUInt32(span);
        span = span[sizeof(uint)..];
        BufferMilliseconds = NetworkBitConverter.ToUInt32(span);
    }

    public override void Serialize(SerializationContext context)
    {
        var length = sizeof(ushort) + sizeof(uint) + sizeof(uint);
        var buffer = ArrayPool.Rent(length);
        try
        {
            var span = buffer.AsSpan();
            NetworkBitConverter.TryGetBytes((ushort)UserControlEventType.StreamBegin, span);
            span = span[sizeof(ushort)..];
            NetworkBitConverter.TryGetBytes(StreamId, span);
            span = span[sizeof(uint)..];
            NetworkBitConverter.TryGetBytes(BufferMilliseconds, span);
        }
        finally
        {
            ArrayPool.Return(buffer);
        }
        context.WriteBuffer.WriteToBuffer(buffer.AsSpan(0, length));
    }
}