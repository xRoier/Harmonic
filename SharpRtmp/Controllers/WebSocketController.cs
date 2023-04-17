using System.Collections.Specialized;
using System.Threading.Tasks;
using SharpRtmp.Networking.Flv;
using SharpRtmp.Networking.WebSocket;

namespace SharpRtmp.Controllers;

public abstract class WebSocketController
{
    public string StreamName { get; internal set; }
    public NameValueCollection Query { get; internal set; }
    public WebSocketSession Session { get; internal set; }

    private FlvMuxer _flvMuxer;
    private FlvDemuxer _flvDemuxer;

    public FlvMuxer FlvMuxer => _flvMuxer ??= new FlvMuxer();
    public FlvDemuxer FlvDemuxer => _flvDemuxer ??= new FlvDemuxer(Session.Options.MessageFactories);

    public abstract Task OnConnect();
    public abstract void OnMessage(string msg);
}