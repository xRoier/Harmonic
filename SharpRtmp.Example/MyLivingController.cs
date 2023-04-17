using SharpRtmp.Controllers.Living;
using SharpRtmp.Rpc;

namespace SharpRtmp.Example;

class MyLivingController : LivingController
{
    [RpcMethod("createStream")]
    public new uint CreateStream()
    {
        var stream = RtmpSession.CreateNetStream<MyLivingStream>();
        return stream.MessageStream.MessageStreamId;
    }
}