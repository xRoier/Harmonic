using Autofac;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Threading;
using SharpRtmp.Controllers;
using SharpRtmp.Networking.Rtmp.Data;
using SharpRtmp.Networking.Rtmp.Messages;
using SharpRtmp.Networking.Rtmp.Messages.Commands;
using SharpRtmp.Rpc;

namespace SharpRtmp.Networking.Rtmp;

public class RtmpSession : IDisposable
{
    internal IoPipeLine IoPipeline { get; set; }
    private readonly Dictionary<uint, RtmpMessageStream> _messageStreams = new();
    internal RtmpControlChunkStream ControlChunkStream { get; }
    public RtmpControlMessageStream ControlMessageStream { get; }
    public NetConnection NetConnection { get; }
    private readonly RpcService _rpcService;
    public ConnectionInformation ConnectionInformation { get; internal set; }
    private readonly object _allocCsidLocker = new();
    private readonly SortedList<uint, uint> _allocatedCsid = new();

    internal RtmpSession(IoPipeLine ioPipeline)
    {
        IoPipeline = ioPipeline;
        ControlChunkStream = new RtmpControlChunkStream(this);
        ControlMessageStream = new RtmpControlMessageStream(this);
        _messageStreams.Add(ControlMessageStream.MessageStreamId, ControlMessageStream);
        NetConnection = new NetConnection(this);
        ControlMessageStream.RegisterMessageHandler<SetChunkSizeMessage>(HandleSetChunkSize);
        ControlMessageStream.RegisterMessageHandler<WindowAcknowledgementSizeMessage>(HandleWindowAcknowledgementSize);
        ControlMessageStream.RegisterMessageHandler<SetPeerBandwidthMessage>(HandleSetPeerBandwidth);
        ControlMessageStream.RegisterMessageHandler<AcknowledgementMessage>(HandleAcknowledgement);
        _rpcService = ioPipeline.Options.ServerLifetime.Resolve<RpcService>();
    }

    private void HandleAcknowledgement(AcknowledgementMessage ack) => Interlocked.Add(ref IoPipeline.ChunkStreamContext.WriteUnAcknowledgedSize, ack.BytesReceived * -1);

    internal void AssertStreamId(uint msid) => Debug.Assert(_messageStreams.ContainsKey(msid));

    internal uint MakeUniqueMessageStreamId()
    {
        // TBD use uint.MaxValue
        return (uint)Random.Shared.Next(1, 20);
    }

    internal uint MakeUniqueChunkStreamId()
    {
        // TBD make csid unique
        lock (_allocCsidLocker)
        {
            var next = _allocatedCsid.Any() ? _allocatedCsid.Last().Key : 2;
            if (uint.MaxValue == next)
            {
                for (uint i = 0; i < uint.MaxValue; i++)
                {
                    if (!_allocatedCsid.ContainsKey(i))
                    {
                        _allocatedCsid.Add(i, i);
                        return i;
                    }
                }
                throw new InvalidOperationException("too many chunk stream");
            }
            next += 1;
            _allocatedCsid.Add(next, next);
            return next;
        }
    }

    public T CreateNetStream<T>() where T: NetStream
    {
        var ret = IoPipeline.Options.ServerLifetime.Resolve<T>();
        ret.MessageStream = CreateMessageStream();
        ret.RtmpSession = this;
        ret.ChunkStream = CreateChunkStream();
        ret.MessageStream.RegisterMessageHandler<CommandMessage>(c => CommandHandler(ret, c));
        NetConnection.AddMessageStream(ret.MessageStream.MessageStreamId, ret);
        return ret;
    }

    public void DeleteNetStream(uint id)
    {
        if (!NetConnection.NetStreams.TryGetValue(id, out var stream))
            return;
        if (stream is IDisposable disp)
            disp.Dispose();
        NetConnection.RemoveMessageStream(id);
    }

    public T CreateCommandMessage<T>() where T: CommandMessage
    {
        var ret = Activator.CreateInstance(typeof(T), ConnectionInformation.AmfEncodingVersion);
        return ret as T;
    }

    public T CreateData<T>() where T : DataMessage
    {
        var ret = Activator.CreateInstance(typeof(T), ConnectionInformation.AmfEncodingVersion);
        return ret as T;
    }

    internal void CommandHandler(RtmpController controller, CommandMessage command)
    {
        try
        {
            _rpcService.PrepareMethod(controller, command, out var method, out var arguments);
            var result = method.Invoke(controller, arguments);
            if (result == null)
                return;
            var resType = method.ReturnType;
            if (resType.IsGenericType && resType.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var tsk = result as Task;
                tsk.ContinueWith(t =>
                {
                    var taskResult = resType.GetProperty("Result")?.GetValue(result);
                    var retCommand = new ReturnResultCommandMessage(command.AmfEncodingVersion)
                    {
                        IsSuccess = true,
                        TransactionId = command.TransactionId,
                        CommandObject = null,
                        ReturnValue = taskResult
                    };
                    _ = controller.MessageStream.SendMessageAsync(controller.ChunkStream, retCommand);
                }, TaskContinuationOptions.OnlyOnRanToCompletion);
                tsk.ContinueWith(t =>
                {
                    var exception = tsk.Exception;
                    var retCommand = new ReturnResultCommandMessage(command.AmfEncodingVersion)
                    {
                        IsSuccess = false,
                        TransactionId = command.TransactionId,
                        CommandObject = null,
                        ReturnValue = exception.Message
                    };
                    _ = controller.MessageStream.SendMessageAsync(controller.ChunkStream, retCommand);
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            else if (resType == typeof(Task))
            {
                var tsk = result as Task;
                tsk.ContinueWith(t =>
                {
                    var exception = tsk.Exception;
                    var retCommand = new ReturnResultCommandMessage(command.AmfEncodingVersion)
                    {
                        IsSuccess = false,
                        TransactionId = command.TransactionId,
                        CommandObject = null,
                        ReturnValue = exception.Message
                    };
                    _ = controller.MessageStream.SendMessageAsync(controller.ChunkStream, retCommand);
                }, TaskContinuationOptions.OnlyOnFaulted);
            }
            else if (resType != typeof(void))
            {
                var retCommand = new ReturnResultCommandMessage(command.AmfEncodingVersion)
                {
                    IsSuccess = true,
                    TransactionId = command.TransactionId,
                    CommandObject = null,
                    ReturnValue = result
                };
                _ = controller.MessageStream.SendMessageAsync(controller.ChunkStream, retCommand);
            }
        }
        catch (Exception e)
        {
            var retCommand = new ReturnResultCommandMessage(command.AmfEncodingVersion)
            {
                IsSuccess = false,
                TransactionId = command.TransactionId,
                CommandObject = null,
                ReturnValue = e.Message
            };
            _ = controller.MessageStream.SendMessageAsync(controller.ChunkStream, retCommand);
        }
    }

    internal bool FindController(string appName, out Type controllerType)
        => IoPipeline.Options.RegisteredControllers.TryGetValue(appName.ToLower(), out controllerType);

    public void Close() => IoPipeline.Disconnect();

    private RtmpMessageStream CreateMessageStream()
    {
        var stream = new RtmpMessageStream(this);
        MessageStreamCreated(stream);
        return stream;
    }

    public RtmpChunkStream CreateChunkStream() => new(this);

    internal void ChunkStreamDestroyed(RtmpChunkStream rtmpChunkStream)
    {
        lock (_allocCsidLocker)
            _allocatedCsid.Remove(rtmpChunkStream.ChunkStreamId);
    }

    internal Task SendMessageAsync(uint chunkStreamId, Message message)
        => IoPipeline.ChunkStreamContext.MultiplexMessageAsync(chunkStreamId, message);

    internal void MessageStreamCreated(RtmpMessageStream messageStream)
        => _messageStreams[messageStream.MessageStreamId] = messageStream;

    internal void MessageStreamDestroying(RtmpMessageStream messageStream)
        => _messageStreams.Remove(messageStream.MessageStreamId);

    internal void MessageArrived(Message message)
    {
        if (message.MessageHeader.MessageStreamId != null &&
            _messageStreams.TryGetValue(message.MessageHeader.MessageStreamId.Value, out var stream))
            stream.MessageArrived(message);
        else
            Console.WriteLine($"Warning: aborted message stream id: {message.MessageHeader.MessageStreamId}");
    }

    internal void Acknowledgement(uint bytesReceived)
    {
        _ = ControlMessageStream.SendMessageAsync(ControlChunkStream, new AcknowledgementMessage
        {
            BytesReceived = bytesReceived
        });
    }

    private void HandleSetPeerBandwidth(SetPeerBandwidthMessage message)
    {
        if (IoPipeline.ChunkStreamContext.WriteWindowAcknowledgementSize.HasValue && message.LimitType == LimitType.Soft && message.WindowSize > IoPipeline.ChunkStreamContext.WriteWindowAcknowledgementSize)
            return;
        if (IoPipeline.ChunkStreamContext.PreviousLimitType.HasValue && message.LimitType == LimitType.Dynamic && IoPipeline.ChunkStreamContext.PreviousLimitType != LimitType.Hard)
            return;
        IoPipeline.ChunkStreamContext.PreviousLimitType = message.LimitType;
        IoPipeline.ChunkStreamContext.WriteWindowAcknowledgementSize = message.WindowSize;
        SendControlMessageAsync(new WindowAcknowledgementSizeMessage
        {
            WindowSize = message.WindowSize
        });
    }

    private void HandleWindowAcknowledgementSize(WindowAcknowledgementSizeMessage message)
        => IoPipeline.ChunkStreamContext.ReadWindowAcknowledgementSize = message.WindowSize;

    private void HandleSetChunkSize(SetChunkSizeMessage setChunkSize)
        => IoPipeline.ChunkStreamContext.ReadChunkSize = (int)setChunkSize.ChunkSize;

    public Task SendControlMessageAsync(Message message)
    {
        if (message.MessageHeader.MessageType == MessageType.WindowAcknowledgementSize)
            IoPipeline.ChunkStreamContext.WriteWindowAcknowledgementSize = ((WindowAcknowledgementSizeMessage)message).WindowSize;
        return ControlMessageStream.SendMessageAsync(ControlChunkStream, message);
    }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposedValue)
            return;
        if (disposing)
        {
            NetConnection.Dispose();
            ControlChunkStream.Dispose();
            ControlMessageStream.Dispose();
        }

        _disposedValue = true;
    }

    public void Dispose() => Dispose(true);
}