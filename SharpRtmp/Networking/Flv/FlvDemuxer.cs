using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SharpRtmp.Hosting;
using SharpRtmp.Networking.Amf.Common;
using SharpRtmp.Networking.Amf.Serialization.Amf0;
using SharpRtmp.Networking.Amf.Serialization.Amf3;
using SharpRtmp.Networking.Flv.Data;
using SharpRtmp.Networking.Rtmp.Data;
using SharpRtmp.Networking.Rtmp.Messages;
using SharpRtmp.Networking.Utils;
using static SharpRtmp.Hosting.RtmpServerOptions;

namespace SharpRtmp.Networking.Flv;

public class FlvDemuxer
{
    private readonly Amf0Reader _amf0Reader = new();
    private readonly Amf3Reader _amf3Reader = new();
    private readonly ArrayPool<byte> _arrayPool = ArrayPool<byte>.Shared;
    private Stream _stream;
    private readonly IReadOnlyDictionary<MessageType, RtmpServerOptions.MessageFactory> _factories;

    public FlvDemuxer(IReadOnlyDictionary<MessageType, RtmpServerOptions.MessageFactory> factories) => _factories = factories;

    public async Task AttachStream(Stream stream, bool disposeOld = false)
    {
        if (disposeOld && _stream != null)
            await _stream.DisposeAsync();
        var headerBuffer = new byte[9];
        await stream.ReadBytesAsync(headerBuffer);
        _stream = stream;
    }

    public void SeekNoLock(double milliseconds, Dictionary<string, object> metaData)
    {
        if (metaData == null)
            return;
        var seconds = milliseconds / 1000;
        var keyframes = metaData["keyframes"] as AmfObject;
        if (keyframes?.Fields["times"] is not List<object> times)
            return;
        var idx = times.FindIndex(t => (double)t >= seconds);
        if (idx == -1)
            return;
        var filePositions = keyframes.Fields["filepositions"] as List<object>;
        var pos = (double)filePositions?[idx]!;
        _stream.Seek((int)(pos - 4), SeekOrigin.Begin);
    }

    private async Task<MessageHeader> ReadHeader(CancellationToken ct = default)
    {
        byte[] headerBuffer = null;
        byte[] timestampBuffer = null;
        try
        {
            headerBuffer = _arrayPool.Rent(15);
            timestampBuffer = _arrayPool.Rent(4);
            await _stream.ReadBytesAsync(headerBuffer.AsMemory(0, 15), ct);
            var type = (MessageType)headerBuffer[4];
            var length = NetworkBitConverter.ToUInt24(headerBuffer.AsSpan(5, 3));

            headerBuffer.AsSpan(8, 3).CopyTo(timestampBuffer.AsSpan(1));
            timestampBuffer[0] = headerBuffer[11];
            var timestamp = NetworkBitConverter.ToInt32(timestampBuffer.AsSpan(0, 4));
            var streamId = NetworkBitConverter.ToUInt24(headerBuffer.AsSpan(12, 3));
            var header = new MessageHeader
            {
                MessageLength = length,
                MessageStreamId = streamId,
                MessageType = type,
                Timestamp = (uint)timestamp
            };
            return header;
        }
        finally
        {
            if (headerBuffer != null)
                _arrayPool.Return(headerBuffer);
            if (timestampBuffer != null)
                _arrayPool.Return(timestampBuffer);
        }
    }

    public FlvAudioData DemultiplexAudioData(AudioMessage message)
    {
        var head = message.Data.Span[0];
        var soundFormat = (SoundFormat)(head >> 4);
        var soundRate = (SoundRate)((head & 0x0C) >> 2);
        var soundSize = (SoundSize)(head & 0x02);
        var soundType = (SoundType)(head & 0x01);
        var ret = new FlvAudioData
        {
            SoundFormat = soundFormat,
            SoundRate = soundRate,
            SoundSize = soundSize,
            SoundType = soundType,
            AudioData = new AudioData()
        };

        if (soundFormat == SoundFormat.Aac)
        {
            ret.AudioData.AacPacketType = (AacPacketType)message.Data.Span[1];
            ret.AudioData.Data = message.Data[2..];
        }
        ret.AudioData.Data = message.Data[1..];
        return ret;
    }

    public FlvVideoData DemultiplexVideoData(VideoMessage message)
    {
        var ret = new FlvVideoData();
        var head = message.Data.Span[0];
        ret.FrameType = (FrameType)(head >> 4);
        ret.CodecId = (CodecId)(head & 0x0F);
        ret.VideoData = message.Data[1..];
        return ret;
    }

    public async Task<Message> DemultiplexFlvAsync(CancellationToken ct = default)
    {
        byte[] bodyBuffer = null;

        try
        {
            var header = await ReadHeader(ct);

            bodyBuffer = _arrayPool.Rent((int)header.MessageLength);
            if (!_factories.TryGetValue(header.MessageType, out var factory))
                throw new InvalidOperationException();

            await _stream.ReadBytesAsync(bodyBuffer.AsMemory(0, (int)header.MessageLength), ct);

            var context = new Rtmp.Serialization.SerializationContext
            {
                Amf0Reader = _amf0Reader,
                Amf3Reader = _amf3Reader,
                ReadBuffer = bodyBuffer.AsMemory(0, (int)header.MessageLength)
            };

            var message = factory(header, context, out var consumed);
            context.ReadBuffer = context.ReadBuffer[consumed..];
            message.MessageHeader = header;
            message.Deserialize(context);
            _amf0Reader.ResetReference();
            _amf3Reader.ResetReference();
            return message;
        }
        finally
        {
            if (bodyBuffer != null)
                _arrayPool.Return(bodyBuffer);
        }
    }
}