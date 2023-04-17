using Harmonic.Controllers;
using Harmonic.Rpc;
using System;

namespace Harmonic.Networking.Rtmp;

public abstract class NetStream : RtmpController, IDisposable
{
    [RpcMethod("deleteStream")]
    public void DeleteStream()
    {
        Dispose();
    }
        
    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposedValue)
            return;
        if (disposing)
            MessageStream.RtmpSession.NetConnection.MessageStreamDestroying(this);

        _disposedValue = true;
    }

    public void Dispose() => Dispose(true);
}