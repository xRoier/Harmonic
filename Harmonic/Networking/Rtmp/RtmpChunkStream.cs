using System;

namespace Harmonic.Networking.Rtmp;

public class RtmpChunkStream : IDisposable
{
    internal RtmpSession RtmpSession { get; set; }
    public uint ChunkStreamId { get; protected set; }

    internal RtmpChunkStream(RtmpSession rtmpSession, uint chunkStreamId)
    {
        ChunkStreamId = chunkStreamId;
        RtmpSession = rtmpSession;
    }

    internal RtmpChunkStream(RtmpSession rtmpSession)
    {
        RtmpSession = rtmpSession;
        ChunkStreamId = rtmpSession.MakeUniqueChunkStreamId();
    }

    protected RtmpChunkStream()
    {

    }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposedValue)
            return;
        if (disposing)
            RtmpSession.ChunkStreamDestroyed(this);

        _disposedValue = true;
    }

    public void Dispose() => Dispose(true);
}