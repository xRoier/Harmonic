using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using SharpRtmp.Networking.Rtmp.Data;
using SharpRtmp.Networking.Rtmp.Serialization;

namespace SharpRtmp.Networking.Rtmp;

public class RtmpMessageStream : IDisposable
{ 
    public uint MessageStreamId { get; private set; }
    internal RtmpSession RtmpSession { get; }
    private readonly Dictionary<MessageType, Action<Message>> _messageHandlers = new();

    internal RtmpMessageStream(RtmpSession rtmpSession, uint messageStreamId)
    {
        MessageStreamId = messageStreamId;
        RtmpSession = rtmpSession;
    }

    internal RtmpMessageStream(RtmpSession rtmpSession)
    {
        MessageStreamId = rtmpSession.MakeUniqueMessageStreamId();
        RtmpSession = rtmpSession;
    }
        
    private void AttachMessage(Message message) => message.MessageHeader.MessageStreamId = MessageStreamId;

    public virtual Task SendMessageAsync(RtmpChunkStream chunkStream, Message message)
    {
        AttachMessage(message);
        return RtmpSession.SendMessageAsync(chunkStream.ChunkStreamId, message);
    }

    internal void RegisterMessageHandler<T>(Action<T> handler) where T: Message
    {
        var attr = typeof(T).GetCustomAttribute<RtmpMessageAttribute>();
        if (attr == null || !attr.MessageTypes.Any())
            throw new InvalidOperationException("unsupported message type");
        foreach (var messageType in attr.MessageTypes)
        {
            if (_messageHandlers.ContainsKey(messageType))
                throw new InvalidOperationException("message type already registered");
            _messageHandlers[messageType] = m => handler(m as T);
        }
    }

    protected void RemoveMessageHandler(MessageType messageType) => _messageHandlers.Remove(messageType);

    internal void MessageArrived(Message message)
    {
        if (_messageHandlers.TryGetValue(message.MessageHeader.MessageType, out var handler))
            handler(message);
    }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposedValue)
            return;
        if (disposing) 
            RtmpSession.MessageStreamDestroying(this);

        _disposedValue = true;
    }

    public void Dispose() => Dispose(true);
}