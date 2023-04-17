using Harmonic.Networking.Flv;
using Harmonic.Networking.Rtmp;
using Harmonic.Rpc;

namespace Harmonic.Controllers;

public abstract class RtmpController
{
    public RtmpMessageStream MessageStream { get; internal set; }
    public RtmpChunkStream ChunkStream { get; internal set; }
    public RtmpSession RtmpSession { get; internal set; }

    private FlvMuxer _flvMuxer;
    private FlvDemuxer _flvDemuxer;

    public FlvMuxer FlvMuxer => _flvMuxer ??= new FlvMuxer();
    public FlvDemuxer FlvDemuxer => _flvDemuxer ??= new FlvDemuxer(RtmpSession.IOPipeline.Options.MessageFactories);

    [RpcMethod("deleteStream")]
    public void DeleteStream([FromOptionalArgument] double streamId) => RtmpSession.DeleteNetStream((uint)streamId);
}