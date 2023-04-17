using SharpRtmp.Rpc;

namespace SharpRtmp.Controllers.Record;

public class RecordController : RtmpController
{
    [RpcMethod("createStream")]
    public uint CreateStream()
    {
        var stream = RtmpSession.CreateNetStream<RecordStream>();
        return stream.MessageStream.MessageStreamId;
    }
}