using Autofac;
using Harmonic.Controllers;
using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Rtmp.Messages.Commands;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Harmonic.Networking.Rtmp;

public class NetConnection : IDisposable
{
    private readonly RtmpSession _rtmpSession;
    private readonly RtmpChunkStream _rtmpChunkStream;
    private readonly Dictionary<uint, RtmpController> _netStreams = new();
    private readonly RtmpControlMessageStream _controlMessageStream;
    public IReadOnlyDictionary<uint, RtmpController> NetStreams => _netStreams;
    private RtmpController _controller;
    private bool _connected;
    private readonly object _streamsLock = new();

    private RtmpController Controller
    {
        get => _controller;
        set
        {
            if (_controller != null)
                throw new InvalidOperationException("already have an controller");

            _controller = value ?? throw new InvalidOperationException("controller cannot be null");
            _controller.MessageStream = _controlMessageStream;
            _controller.ChunkStream = _rtmpChunkStream;
            _controller.RtmpSession = _rtmpSession;
        }
    }

    internal NetConnection(RtmpSession rtmpSession)
    {
        _rtmpSession = rtmpSession;
        _rtmpChunkStream = _rtmpSession.CreateChunkStream();
        _controlMessageStream = _rtmpSession.ControlMessageStream;
        _controlMessageStream.RegisterMessageHandler<CommandMessage>(CommandHandler);
    }

    private void CommandHandler(CommandMessage command)
    {
        switch (command.ProcedureName)
        {
            case "connect":
                Connect(command);
                _connected = true;
                break;
            case "close":
                Close();
                _connected = false;
                break;
            default:
            {
                if (_controller != null && _connected)
                    _rtmpSession.CommandHandler(_controller, command);
                else
                    _rtmpSession.Close();
                break;
            }
        }
    }

    public void Connect(CommandMessage command)
    {
        var commandObj = command.CommandObject;
        _rtmpSession.ConnectionInformation = new Networking.ConnectionInformation();
        var props = _rtmpSession.ConnectionInformation.GetType().GetProperties();
        foreach (var prop in props)
        {
            var sb = new StringBuilder(prop.Name);
            sb[0] = char.ToLower(sb[0]);
            var asPropName = sb.ToString();
            if (!commandObj.Fields.ContainsKey(asPropName))
                continue;
            var commandObjectValue = commandObj.Fields[asPropName];
            if (commandObjectValue.GetType() == prop.PropertyType)
                prop.SetValue(_rtmpSession.ConnectionInformation, commandObjectValue);
        }

        if (_rtmpSession.FindController(_rtmpSession.ConnectionInformation.App, out var controllerType))
            Controller = _rtmpSession.IoPipeline.Options.ServerLifetime.Resolve(controllerType) as RtmpController;
        else
        {
            _rtmpSession.Close();
            return;
        }
        var param = new AmfObject
        {
            { "code", "NetConnection.Connect.Success" },
            { "description", "Connection succeeded." },
            { "level", "status" },
        };

        var msg = _rtmpSession.CreateCommandMessage<ReturnResultCommandMessage>();
        msg.CommandObject = new AmfObject
        {
            { "capabilities", 255.00 },
            { "fmsVer", "FMS/4,5,1,484" },
            { "mode", 1.0 }
        };
        msg.ReturnValue = param;
        msg.IsSuccess = true;
        msg.TransactionId = command.TransactionId;
        _rtmpSession.ControlMessageStream.SendMessageAsync(_rtmpChunkStream, msg);
    }

    public void Close() => _rtmpSession.Close();

    internal void MessageStreamDestroying(NetStream stream)
    {
        lock (_streamsLock)
            _netStreams.Remove(stream.MessageStream.MessageStreamId);
    }

    internal void AddMessageStream(uint id, NetStream stream)
    {
        lock (_streamsLock)
            _netStreams.Add(id, stream);
    }

    internal void RemoveMessageStream(uint id)
    {
        lock (_streamsLock)
            _netStreams.Remove(id);
    }

    private bool _disposedValue;

    protected virtual void Dispose(bool disposing)
    {
        if (_disposedValue)
            return;
        if (disposing)
        {
            lock (_streamsLock)
            {
                while (_netStreams.Any())
                {
                    var (_, stream) = _netStreams.First();
                    if (stream is IDisposable disp)
                        disp.Dispose();
                }
            }

            _rtmpChunkStream.Dispose();
        }

        _disposedValue = true;
    }

    public void Dispose() => Dispose(true);
}