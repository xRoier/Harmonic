using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using SharpRtmp.Buffers;
using SharpRtmp.Networking.Amf.Serialization.Amf0;
using SharpRtmp.Networking.Amf.Serialization.Amf3;
using SharpRtmp.Networking.Rtmp.Data;
using SharpRtmp.Networking.Rtmp.Messages;
using SharpRtmp.Networking.Rtmp.Serialization;
using SharpRtmp.Networking.Utils;

namespace SharpRtmp.Networking.Rtmp;

class ChunkStreamContext : IDisposable
{
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    internal ChunkHeader ProcessingChunk;
    internal int ReadMinimumBufferSize => (ReadChunkSize + Type0Size) * 4;
    private readonly Dictionary<uint, MessageHeader> _previousWriteMessageHeader = new();
    private readonly Dictionary<uint, MessageHeader> _previousReadMessageHeader = new();
    private readonly Dictionary<uint, MessageReadingState> _incompleteMessageState = new();
    internal uint? ReadWindowAcknowledgementSize { get; set; }
    internal uint? WriteWindowAcknowledgementSize { get; set; }
    internal int ReadChunkSize { get; set; } = 128;
    internal long ReadUnAcknowledgedSize = 0;
    internal long WriteUnAcknowledgedSize;

    private uint _writeChunkSize = 128;
    private const int ExtendedTimestampLength = 4;
    private const int Type0Size = 11;
    private const int Type1Size = 7;
    private const int Type2Size = 3;

    public readonly RtmpSession RtmpSession;

    private readonly Amf0Reader _amf0Reader = new();
    private readonly Amf0Writer _amf0Writer = new();
    private readonly Amf3Reader _amf3Reader = new();
    private readonly Amf3Writer _amf3Writer = new();


    private readonly IoPipeLine _ioPipeline;
    private readonly SemaphoreSlim _sync = new(1);
    internal LimitType? PreviousLimitType { get; set; }

    public ChunkStreamContext(IoPipeLine stream)
    {
        RtmpSession = new RtmpSession(stream);
        _ioPipeline = stream;
        _ioPipeline.NextProcessState = ProcessState.FirstByteBasicHeader;
        _ioPipeline.BufferProcessors.Add(ProcessState.ChunkMessageHeader, ProcessChunkMessageHeader);
        _ioPipeline.BufferProcessors.Add(ProcessState.CompleteMessage, ProcessCompleteMessage);
        _ioPipeline.BufferProcessors.Add(ProcessState.ExtendedTimestamp, ProcessExtendedTimestamp);
        _ioPipeline.BufferProcessors.Add(ProcessState.FirstByteBasicHeader, ProcessFirstByteBasicHeader);
    }

    public void Dispose() => ((IDisposable)RtmpSession).Dispose();

    internal async Task MultiplexMessageAsync(uint chunkStreamId, Message message)
    {
        if (!message.MessageHeader.MessageStreamId.HasValue)
            throw new InvalidOperationException("cannot send message that has not attached to a message stream");
        byte[] buffer;
        uint length;
        using (var writeBuffer = new ByteBuffer())
        {
            var context = new Serialization.SerializationContext
            {
                Amf0Reader = _amf0Reader,
                Amf0Writer = _amf0Writer,
                Amf3Reader = _amf3Reader,
                Amf3Writer = _amf3Writer,
                WriteBuffer = writeBuffer
            };
            message.Serialize(context);
            length = (uint)writeBuffer.Length;
            Debug.Assert(length != 0);
            buffer = _arrayPool.Rent((int)length);
            writeBuffer.TakeOutMemory(buffer);
        }

        try
        {
            message.MessageHeader.MessageLength = length;
            Debug.Assert(message.MessageHeader.MessageLength != 0);
            if (message.MessageHeader.MessageType == 0)
                message.MessageHeader.MessageType = message.GetType().GetCustomAttribute<RtmpMessageAttribute>()!.MessageTypes.First();
            Debug.Assert(message.MessageHeader.MessageType != 0);
            Task ret = null;
            // chunking
            var isFirstChunk = true;
            RtmpSession.AssertStreamId(message.MessageHeader.MessageStreamId.Value);
            for (var i = 0; i < message.MessageHeader.MessageLength;)
            {
                _previousWriteMessageHeader.TryGetValue(chunkStreamId, out var prevHeader);
                var chunkHeaderType = SelectChunkType(message.MessageHeader, prevHeader, isFirstChunk);
                isFirstChunk = false;
                GenerateBasicHeader(chunkHeaderType, chunkStreamId, out var basicHeader, out var basicHeaderLength);
                GenerateMesesageHeader(chunkHeaderType, message.MessageHeader, prevHeader, out var messageHeader, out var messageHeaderLength);
                _previousWriteMessageHeader[chunkStreamId] = (MessageHeader)message.MessageHeader.Clone();
                var headerLength = basicHeaderLength + messageHeaderLength;
                var bodySize = (int)(length - i >= _writeChunkSize ? _writeChunkSize : length - i);

                var chunkBuffer = _arrayPool.Rent(headerLength + bodySize);
                await _sync.WaitAsync();
                try
                {
                    basicHeader.AsSpan(0, basicHeaderLength).CopyTo(chunkBuffer);
                    messageHeader.AsSpan(0, messageHeaderLength).CopyTo(chunkBuffer.AsSpan(basicHeaderLength));
                    _arrayPool.Return(basicHeader);
                    _arrayPool.Return(messageHeader);
                    buffer.AsSpan(i, bodySize).CopyTo(chunkBuffer.AsSpan(headerLength));
                    i += bodySize;
                    var isLastChunk = message.MessageHeader.MessageLength - i == 0;

                    long offset = 0;
                    long totalLength = headerLength + bodySize;
                    var currentSendSize = totalLength;

                    while (offset != headerLength + bodySize)
                    {
                        if (WriteWindowAcknowledgementSize.HasValue && Interlocked.Read(ref WriteUnAcknowledgedSize) + headerLength + bodySize > WriteWindowAcknowledgementSize.Value)
                        {
                            currentSendSize = Math.Min(WriteWindowAcknowledgementSize.Value, currentSendSize);
                            //var delayCount = 0;
                            while (currentSendSize + Interlocked.Read(ref WriteUnAcknowledgedSize) >= WriteWindowAcknowledgementSize.Value)
                                await Task.Delay(1);
                        }
                        var tsk = _ioPipeline.SendRawData(chunkBuffer.AsMemory((int)offset, (int)currentSendSize));
                        offset += currentSendSize;
                        totalLength -= currentSendSize;

                        if (WriteWindowAcknowledgementSize.HasValue)
                            Interlocked.Add(ref WriteUnAcknowledgedSize, currentSendSize);

                        if (isLastChunk)
                            ret = tsk;
                    }
                    if (isLastChunk)
                    {
                        switch (message.MessageHeader.MessageType)
                        {
                            case MessageType.SetChunkSize:
                            {
                                var setChunkSize = message as SetChunkSizeMessage;
                                _writeChunkSize = setChunkSize.ChunkSize;
                                break;
                            }
                            case MessageType.SetPeerBandwidth:
                            {
                                var m = message as SetPeerBandwidthMessage;
                                ReadWindowAcknowledgementSize = m.WindowSize;
                                break;
                            }
                            case MessageType.WindowAcknowledgementSize:
                            {
                                var m = message as WindowAcknowledgementSizeMessage;
                                WriteWindowAcknowledgementSize = m.WindowSize;
                                break;
                            }
                        }
                    }
                }
                finally
                {
                    _sync.Release();
                    _arrayPool.Return(chunkBuffer);
                }
            }
            Debug.Assert(ret != null);
            await ret;
        }
        finally
        {
            _arrayPool.Return(buffer);
        }

    }

    private void GenerateMesesageHeader(ChunkHeaderType chunkHeaderType, MessageHeader header, MessageHeader prevHeader, out byte[] buffer, out int length)
    {
        var timestamp = header.Timestamp;
        switch (chunkHeaderType)
        {
            case ChunkHeaderType.Type0:
                buffer = _arrayPool.Rent(Type0Size + ExtendedTimestampLength);
                NetworkBitConverter.TryGetUInt24Bytes(timestamp >= 0xFFFFFF ? 0xFFFFFF : timestamp, buffer.AsSpan(0, 3));
                NetworkBitConverter.TryGetUInt24Bytes(header.MessageLength, buffer.AsSpan(3, 3));
                NetworkBitConverter.TryGetBytes((byte)header.MessageType, buffer.AsSpan(6, 1));
                NetworkBitConverter.TryGetBytes(header.MessageStreamId.Value, buffer.AsSpan(7, 4), true);
                length = Type0Size;
                break;
            case ChunkHeaderType.Type1:
                buffer = _arrayPool.Rent(Type1Size + ExtendedTimestampLength);
                timestamp = timestamp - prevHeader.Timestamp;
                NetworkBitConverter.TryGetUInt24Bytes(timestamp >= 0xFFFFFF ? 0xFFFFFF : timestamp, buffer.AsSpan(0, 3));
                NetworkBitConverter.TryGetUInt24Bytes(header.MessageLength, buffer.AsSpan(3, 3));
                NetworkBitConverter.TryGetBytes((byte)header.MessageType, buffer.AsSpan(6, 1));
                length = Type1Size;
                break;
            case ChunkHeaderType.Type2:
                buffer = _arrayPool.Rent(Type2Size + ExtendedTimestampLength);
                timestamp = timestamp - prevHeader.Timestamp;
                NetworkBitConverter.TryGetUInt24Bytes(timestamp >= 0xFFFFFF ? 0xFFFFFF : timestamp, buffer.AsSpan(0, 3));
                length = Type2Size;
                break;
            case ChunkHeaderType.Type3:
                buffer = _arrayPool.Rent(ExtendedTimestampLength);
                length = 0;
                break;
            default:
                throw new ArgumentException();
        }

        if (timestamp < 0xFFFFFF)
            return;
        NetworkBitConverter.TryGetBytes(timestamp, buffer.AsSpan(length, ExtendedTimestampLength));
        length += ExtendedTimestampLength;
    }

    private void GenerateBasicHeader(ChunkHeaderType chunkHeaderType, uint chunkStreamId, out byte[] buffer, out int length)
    {
        var fmt = (byte)chunkHeaderType;
        switch (chunkStreamId)
        {
            case >= 2 and <= 63:
                buffer = _arrayPool.Rent(1);
                buffer[0] = (byte)((byte)(fmt << 6) | chunkStreamId);
                length = 1;
                break;
            case >= 64 and <= 319:
                buffer = _arrayPool.Rent(2);
                buffer[0] = (byte)(fmt << 6);
                buffer[1] = (byte)(chunkStreamId - 64);
                length = 2;
                break;
            case >= 320 and <= 65599:
                buffer = _arrayPool.Rent(3);
                buffer[0] = (byte)((fmt << 6) | 1);
                buffer[1] = (byte)((chunkStreamId - 64) & 0xff);
                buffer[2] = (byte)((chunkStreamId - 64) >> 8);
                length = 3;
                break;
            default:
                throw new NotSupportedException();
        }
    }

    private ChunkHeaderType SelectChunkType(MessageHeader messageHeader, MessageHeader prevHeader, bool isFirstChunk)
    {
        if (prevHeader == null)
            return ChunkHeaderType.Type0;

        if (!isFirstChunk)
            return ChunkHeaderType.Type3;

        long currentTimestamp = messageHeader.Timestamp;
        long prevTimesatmp = prevHeader.Timestamp;

        if (currentTimestamp - prevTimesatmp < 0)
            return ChunkHeaderType.Type0;

        if (messageHeader.MessageType == prevHeader.MessageType &&
            messageHeader.MessageLength == prevHeader.MessageLength &&
            messageHeader.MessageStreamId == prevHeader.MessageStreamId &&
            messageHeader.Timestamp != prevHeader.Timestamp)
            return ChunkHeaderType.Type2;
        
        return messageHeader.MessageStreamId == prevHeader.MessageStreamId ? ChunkHeaderType.Type1 : ChunkHeaderType.Type0;
    }
    private void FillHeader(ChunkHeader header)
    {
        if (!_previousReadMessageHeader.TryGetValue(header.ChunkBasicHeader.ChunkStreamId, out var prevHeader) &&
            header.ChunkBasicHeader.RtmpChunkHeaderType != ChunkHeaderType.Type0)
            throw new InvalidOperationException();

        switch (header.ChunkBasicHeader.RtmpChunkHeaderType)
        {
            case ChunkHeaderType.Type1:
                header.MessageHeader.Timestamp += prevHeader.Timestamp;
                header.MessageHeader.MessageStreamId = prevHeader.MessageStreamId;
                break;
            case ChunkHeaderType.Type2:
                header.MessageHeader.Timestamp += prevHeader.Timestamp;
                header.MessageHeader.MessageLength = prevHeader.MessageLength;
                header.MessageHeader.MessageType = prevHeader.MessageType;
                header.MessageHeader.MessageStreamId = prevHeader.MessageStreamId;
                break;
            case ChunkHeaderType.Type3:
                header.MessageHeader.Timestamp = prevHeader.Timestamp;
                header.MessageHeader.MessageLength = prevHeader.MessageLength;
                header.MessageHeader.MessageType = prevHeader.MessageType;
                header.MessageHeader.MessageStreamId = prevHeader.MessageStreamId;
                break;
        }
    }


    public bool ProcessFirstByteBasicHeader(ReadOnlySequence<byte> buffer, ref int consumed)
    {
        if (buffer.Length - consumed < 1)
            return false;
        var header = new ChunkHeader
        {
            ChunkBasicHeader = new ChunkBasicHeader(),
            MessageHeader = new MessageHeader()
        };
        ProcessingChunk = header;
        var arr = _arrayPool.Rent(1);
        buffer.Slice(consumed, 1).CopyTo(arr);
        consumed += 1;
        var basicHeader = arr[0];
        _arrayPool.Return(arr);
        header.ChunkBasicHeader.RtmpChunkHeaderType = (ChunkHeaderType)(basicHeader >> 6);
        header.ChunkBasicHeader.ChunkStreamId = (uint)basicHeader & 0x3F;
        if (header.ChunkBasicHeader.ChunkStreamId != 0 && header.ChunkBasicHeader.ChunkStreamId != 0x3F)
        {
            if (header.ChunkBasicHeader.RtmpChunkHeaderType == ChunkHeaderType.Type3)
            {
                FillHeader(header);
                _ioPipeline.NextProcessState = ProcessState.CompleteMessage;
                return true;
            }
        }
        _ioPipeline.NextProcessState = ProcessState.ChunkMessageHeader;
        return true;
    }

    private bool ProcessChunkMessageHeader(ReadOnlySequence<byte> buffer, ref int consumed)
    {
        var bytesNeed = 0;
        switch (ProcessingChunk.ChunkBasicHeader.ChunkStreamId)
        {
            case 0:
                bytesNeed = 1;
                break;
            case 0x3F:
                bytesNeed = 2;
                break;
        }
        switch (ProcessingChunk.ChunkBasicHeader.RtmpChunkHeaderType)
        {
            case ChunkHeaderType.Type0:
                bytesNeed += Type0Size;
                break;
            case ChunkHeaderType.Type1:
                bytesNeed += Type1Size;
                break;
            case ChunkHeaderType.Type2:
                bytesNeed += Type2Size;
                break;
        }

        if (buffer.Length - consumed <= bytesNeed)
            return false;

        byte[] arr = null;
        switch (ProcessingChunk.ChunkBasicHeader.ChunkStreamId)
        {
            case 0:
                arr = _arrayPool.Rent(1);
                buffer.Slice(consumed, 1).CopyTo(arr);
                consumed += 1;
                ProcessingChunk.ChunkBasicHeader.ChunkStreamId = (uint)arr[0] + 64;
                _arrayPool.Return(arr);
                break;
            case 0x3F:
                arr = _arrayPool.Rent(2);
                buffer.Slice(consumed, 2).CopyTo(arr);
                consumed += 2;
                ProcessingChunk.ChunkBasicHeader.ChunkStreamId = (uint)arr[1] * 256 + arr[0] + 64;
                _arrayPool.Return(arr);
                break;
        }
        var header = ProcessingChunk;
        switch (header.ChunkBasicHeader.RtmpChunkHeaderType)
        {
            case ChunkHeaderType.Type0:
                arr = _arrayPool.Rent(Type0Size);
                buffer.Slice(consumed, Type0Size).CopyTo(arr);
                consumed += Type0Size;
                header.MessageHeader.Timestamp = NetworkBitConverter.ToUInt24(arr.AsSpan(0, 3));
                header.MessageHeader.MessageLength = NetworkBitConverter.ToUInt24(arr.AsSpan(3, 3));
                header.MessageHeader.MessageType = (MessageType)arr[6];
                header.MessageHeader.MessageStreamId = NetworkBitConverter.ToUInt32(arr.AsSpan(7, 4), true);
                break;
            case ChunkHeaderType.Type1:
                arr = _arrayPool.Rent(Type1Size);
                buffer.Slice(consumed, Type1Size).CopyTo(arr);
                consumed += Type1Size;
                header.MessageHeader.Timestamp = NetworkBitConverter.ToUInt24(arr.AsSpan(0, 3));
                header.MessageHeader.MessageLength = NetworkBitConverter.ToUInt24(arr.AsSpan(3, 3));
                header.MessageHeader.MessageType = (MessageType)arr[6];
                break;
            case ChunkHeaderType.Type2:
                arr = _arrayPool.Rent(Type2Size);
                buffer.Slice(consumed, Type2Size).CopyTo(arr);
                consumed += Type2Size;
                header.MessageHeader.Timestamp = NetworkBitConverter.ToUInt24(arr.AsSpan(0, 3));
                break;
        }
        if (arr != null)
            _arrayPool.Return(arr);
        FillHeader(header);
        _ioPipeline.NextProcessState = header.MessageHeader.Timestamp == 0x00FFFFFF ? ProcessState.ExtendedTimestamp : ProcessState.CompleteMessage;
        return true;
    }

    private bool ProcessExtendedTimestamp(ReadOnlySequence<byte> buffer, ref int consumed)
    {
        if (buffer.Length - consumed < 4)
            return false;
        var arr = _arrayPool.Rent(4);
        buffer.Slice(consumed, 4).CopyTo(arr);
        consumed += 4;
        var extendedTimestamp = NetworkBitConverter.ToUInt32(arr.AsSpan(0, 4));
        ProcessingChunk.ExtendedTimestamp = extendedTimestamp;
        ProcessingChunk.MessageHeader.Timestamp = extendedTimestamp;
        _ioPipeline.NextProcessState = ProcessState.CompleteMessage;
        return true;
    }

    private bool ProcessCompleteMessage(ReadOnlySequence<byte> buffer, ref int consumed)
    {
        var header = ProcessingChunk;
        if (!_incompleteMessageState.TryGetValue(header.ChunkBasicHeader.ChunkStreamId, out var state))
        {
            state = new MessageReadingState
            {
                CurrentIndex = 0,
                MessageLength = header.MessageHeader.MessageLength,
                Body = _arrayPool.Rent((int)header.MessageHeader.MessageLength)
            };
            _incompleteMessageState.Add(header.ChunkBasicHeader.ChunkStreamId, state);
        }

        var bytesNeed = (int)(state.RemainBytes >= ReadChunkSize ? ReadChunkSize : state.RemainBytes);

        if (buffer.Length - consumed < bytesNeed)
            return false;

        if (_previousReadMessageHeader.TryGetValue(header.ChunkBasicHeader.ChunkStreamId, out var prevHeader))
        {
            if (prevHeader.MessageStreamId != header.MessageHeader.MessageStreamId)
            {
                // inform user previous message will never be received
                prevHeader = null;
            }
        }
        _previousReadMessageHeader[ProcessingChunk.ChunkBasicHeader.ChunkStreamId] = (MessageHeader)ProcessingChunk.MessageHeader.Clone();
        ProcessingChunk = null;

        buffer.Slice(consumed, bytesNeed).CopyTo(state.Body.AsSpan(state.CurrentIndex));
        consumed += bytesNeed;
        state.CurrentIndex += bytesNeed;

        if (state.IsCompleted)
        {
            _incompleteMessageState.Remove(header.ChunkBasicHeader.ChunkStreamId);
            try
            {
                var context = new Serialization.SerializationContext()
                {
                    Amf0Reader = _amf0Reader,
                    Amf0Writer = _amf0Writer,
                    Amf3Reader = _amf3Reader,
                    Amf3Writer = _amf3Writer,
                    ReadBuffer = state.Body.AsMemory(0, (int)state.MessageLength)
                };
                if (header.MessageHeader.MessageType == MessageType.AggregateMessage)
                {
                    var agg = new AggregateMessage()
                    {
                        MessageHeader = header.MessageHeader
                    };
                    agg.Deserialize(context);
                    foreach (var message in agg.Messages)
                    {
                        if (!_ioPipeline.Options.MessageFactories.TryGetValue(message.Header.MessageType, out var factory))
                        {
                            continue;
                        }
                        var msgContext = new Serialization.SerializationContext()
                        {
                            Amf0Reader = context.Amf0Reader,
                            Amf3Reader = context.Amf3Reader,
                            Amf0Writer = context.Amf0Writer,
                            Amf3Writer = context.Amf3Writer,
                            ReadBuffer = context.ReadBuffer.Slice(message.DataOffset, (int)message.DataLength)
                        };
                        try
                        {
                            var msg = factory(header.MessageHeader, msgContext, out var factoryConsumed);
                            msg.MessageHeader = header.MessageHeader;
                            msg.Deserialize(msgContext);
                            context.Amf0Reader.ResetReference();
                            context.Amf3Reader.ResetReference();
                            RtmpSession.MessageArrived(msg);
                        }
                        catch (NotSupportedException)
                        {
                        }
                    }
                }
                else
                {
                    if (_ioPipeline.Options.MessageFactories.TryGetValue(header.MessageHeader.MessageType, out var factory))
                    {
                        try
                        {
                            var message = factory(header.MessageHeader, context, out var factoryConsumed);
                            message.MessageHeader = header.MessageHeader;
                            context.ReadBuffer = context.ReadBuffer[factoryConsumed..];
                            message.Deserialize(context);
                            context.Amf0Reader.ResetReference();
                            context.Amf3Reader.ResetReference();
                            RtmpSession.MessageArrived(message);
                        }
                        catch (NotSupportedException)
                        {
                        }
                    }
                }
            }
            finally
            {
                _arrayPool.Return(state.Body);
            }
        }
        _ioPipeline.NextProcessState = ProcessState.FirstByteBasicHeader;
        return true;
    }
}