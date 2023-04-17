using System;
using SharpRtmp.Buffers;

namespace SharpRtmp.Networking.Amf.Data;

public interface IExternalizable
{
    bool TryDecodeData(Span<byte> buffer, out int consumed);

    bool TryEncodeData(ByteBuffer buffer);
}