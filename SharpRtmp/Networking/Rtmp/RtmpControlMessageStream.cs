namespace SharpRtmp.Networking.Rtmp;

public class RtmpControlMessageStream : RtmpMessageStream
{
    private const uint ControlMsid = 0;

    internal RtmpControlMessageStream(RtmpSession rtmpSession) : base(rtmpSession, ControlMsid)
    {
    }
}