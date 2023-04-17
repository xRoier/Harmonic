﻿using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SharpRtmp.Networking.Amf.Common;
using SharpRtmp.Networking.Rtmp;
using SharpRtmp.Networking.Rtmp.Data;
using SharpRtmp.Networking.Rtmp.Messages;
using SharpRtmp.Networking.Rtmp.Messages.Commands;
using SharpRtmp.Networking.Rtmp.Messages.UserControlMessages;
using SharpRtmp.Networking.Rtmp.Streaming;
using SharpRtmp.Rpc;
using SharpRtmp.Service;

namespace SharpRtmp.Controllers.Living;

public class LivingStream : NetStream
{
    private readonly List<Action> _cleanupActions = new();
    private PublishingType _publishingType;
    private readonly PublisherSessionService _publisherSessionService;
    public DataMessage FlvMetadata;
    public AudioMessage AacConfigureRecord;
    public VideoMessage AvcConfigureRecord;
    public event Action<VideoMessage> OnVideoMessage;
    public event Action<AudioMessage> OnAudioMessage;
    private RtmpChunkStream _videoChunkStream;
    private RtmpChunkStream _audioChunkStream;

    public LivingStream(PublisherSessionService publisherSessionService)
    {
        _publisherSessionService = publisherSessionService;
        _audioChunkStream = null;
    }

    [RpcMethod("play")]
    public async Task Play(
        [FromOptionalArgument] string streamName,
        [FromOptionalArgument] double start = -1,
        [FromOptionalArgument] double duration = -1,
        [FromOptionalArgument] bool reset = false)
    {
        var publisher = _publisherSessionService.FindPublisher(streamName);

        if (publisher == null)
            throw new KeyNotFoundException();

        var resetData = new AmfObject
        {
            {"level", "status" },
            {"code", "NetStream.Play.Reset" },
            {"description", "Resetting and playing stream." },
            {"details", streamName }
        };
        var resetStatus = RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
        resetStatus.InfoObject = resetData;
        await MessageStream.SendMessageAsync(ChunkStream, resetStatus);

        var startData = new AmfObject
        {
            {"level", "status" },
            {"code", "NetStream.Play.Start" },
            {"description", "Started playing." },
            {"details", streamName }
        };
        var startStatus = RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
        startStatus.InfoObject = startData;
        await MessageStream.SendMessageAsync(ChunkStream, startStatus);

        var flvMetadata = RtmpSession.CreateData<DataMessage>();
        flvMetadata.MessageHeader = (MessageHeader)publisher.FlvMetadata.MessageHeader.Clone();
        flvMetadata.Data = publisher.FlvMetadata.Data;
        await MessageStream.SendMessageAsync(ChunkStream, flvMetadata);

        _videoChunkStream = RtmpSession.CreateChunkStream();
        _audioChunkStream = RtmpSession.CreateChunkStream();

        if (publisher.AacConfigureRecord != null)
            await MessageStream.SendMessageAsync(_audioChunkStream, publisher.AacConfigureRecord.Clone() as AudioMessage);

        if (publisher.AvcConfigureRecord != null)
            await MessageStream.SendMessageAsync(_videoChunkStream, publisher.AvcConfigureRecord.Clone() as VideoMessage);

        publisher.OnAudioMessage += SendAudio;
        publisher.OnVideoMessage += SendVideo;
        _cleanupActions.Add(() =>
        {
            publisher.OnVideoMessage -= SendVideo;
            publisher.OnAudioMessage -= SendAudio;
        });
    }

    private async void SendVideo(VideoMessage message)
    {
        var video = message.Clone() as VideoMessage;
            
        try
        {
            await MessageStream.SendMessageAsync(_videoChunkStream, video);
        }
        catch
        {
            foreach (var a in _cleanupActions)
            {
                a();
            }
            RtmpSession.Close();
        }
    }

    private async void SendAudio(AudioMessage message)
    {
        var audio = message.Clone();
        try
        {
            await MessageStream.SendMessageAsync(_audioChunkStream, audio as AudioMessage);
        }
        catch
        {
            foreach (var a in _cleanupActions)
            {
                a();
            }
            RtmpSession.Close();
        }
    }

    [RpcMethod(Name = "publish")]
    public void Publish([FromOptionalArgument] string publishingName, [FromOptionalArgument] string publishingType)
    {
        if (string.IsNullOrEmpty(publishingName))
            throw new InvalidOperationException("empty publishing name");
        if (!PublishingHelpers.IsTypeSupported(publishingType))
            throw new InvalidOperationException($"not supported publishing type {publishingType}");

        _publishingType = PublishingHelpers.PublishingTypes[publishingType];

        _publisherSessionService.RegisterPublisher(publishingName, this);

        RtmpSession.SendControlMessageAsync(new StreamBeginMessage() { StreamId = MessageStream.MessageStreamId });
        var onStatus = RtmpSession.CreateCommandMessage<OnStatusCommandMessage>();
        MessageStream.RegisterMessageHandler<DataMessage>(HandleDataMessage);
        MessageStream.RegisterMessageHandler<AudioMessage>(HandleAudioMessage);
        MessageStream.RegisterMessageHandler<VideoMessage>(HandleVideoMessage);
        onStatus.InfoObject = new AmfObject
        {
            {"level", "status" },
            {"code", "NetStream.Publish.Start" },
            {"description", "Stream is now published." },
            {"details", publishingName }
        };
        MessageStream.SendMessageAsync(ChunkStream, onStatus);
    }

    private void HandleDataMessage(DataMessage msg)
    {
        FlvMetadata = msg;
    }

    private void HandleAudioMessage(AudioMessage audioData)
    {
        if (AacConfigureRecord == null && audioData.Data.Length >= 2)
        {
            AacConfigureRecord = audioData;
            return;
        }
        OnAudioMessage?.Invoke(audioData);
    }

    private void HandleVideoMessage(VideoMessage videoData)
    {
        if (AvcConfigureRecord == null && videoData.Data.Length >= 2)
            AvcConfigureRecord = videoData;
        OnVideoMessage?.Invoke(videoData);
    }

    private bool _disposedValue;

    protected override void Dispose(bool disposing)
    {
        if (_disposedValue) return;
        base.Dispose(disposing);
        _publisherSessionService.RemovePublisher(this);
        _videoChunkStream?.Dispose();
        _audioChunkStream?.Dispose();

        _disposedValue = true;
    }
}