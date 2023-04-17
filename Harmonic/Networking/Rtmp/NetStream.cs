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
        
    private bool disposedValue = false;

    protected virtual void Dispose(bool disposing)
    {
        if (!disposedValue)
        {
            if (disposing)
            {
                MessageStream.RtmpSession.NetConnection.MessageStreamDestroying(this);
            }

            disposedValue = true;
        }
    }

    public void Dispose() => Dispose(true);
}