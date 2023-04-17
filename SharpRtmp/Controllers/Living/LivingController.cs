using SharpRtmp.Rpc;

namespace SharpRtmp.Controllers.Living;

public class LivingController : RtmpController
{
    [RpcMethod("createStream")]
    public uint CreateStream()
    {
        var stream = RtmpSession.CreateNetStream<LivingStream>();
        return stream.MessageStream.MessageStreamId;
    }
}