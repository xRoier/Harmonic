using System.Net.Sockets;
using System.Threading;

namespace SharpRtmp.Networking.Rtmp.Data;

public class SocketState
{
    public SocketState(Socket listener, CancellationToken ct)
    {
        Socket = listener;
        CancellationToken = ct;
    }

    public Socket Socket { get; set; }
    public CancellationToken CancellationToken { get; set; }
}