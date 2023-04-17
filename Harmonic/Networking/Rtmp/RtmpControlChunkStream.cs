namespace Harmonic.Networking.Rtmp;

class RtmpControlChunkStream : RtmpChunkStream
{
    private const uint ControlCsid = 2;

    internal RtmpControlChunkStream(RtmpSession rtmpSession)
    {
        ChunkStreamId = ControlCsid;
        RtmpSession = rtmpSession;
    }
}