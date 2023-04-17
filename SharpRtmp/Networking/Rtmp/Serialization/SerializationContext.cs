using System;
using SharpRtmp.Buffers;
using SharpRtmp.Networking.Amf.Serialization.Amf0;
using SharpRtmp.Networking.Amf.Serialization.Amf3;

namespace SharpRtmp.Networking.Rtmp.Serialization;

public class SerializationContext
{
    public Amf3Reader Amf3Reader { get; internal set; } = null;
    public Amf3Writer Amf3Writer { get; internal set; } = null;
    public Amf0Reader Amf0Reader { get; internal set; } = null;
    public Amf0Writer Amf0Writer { get; internal set; } = null;

    public ByteBuffer WriteBuffer { get; internal set; } = null;
    public Memory<byte> ReadBuffer { get; internal set; } = null;

}