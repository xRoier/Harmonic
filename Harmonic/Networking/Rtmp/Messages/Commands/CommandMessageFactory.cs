using System;
using System.Collections.Generic;
using System.Net;
using System.Reflection;
using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;

namespace Harmonic.Networking.Rtmp.Messages.Commands;

public class CommandMessageFactory
{
    public readonly Dictionary<string, Type> MessageFactories = new();

    public CommandMessageFactory()
    {
        RegisterMessage<ConnectCommandMessage>();
        RegisterMessage<CreateStreamCommandMessage>();
        RegisterMessage<DeleteStreamCommandMessage>();
        RegisterMessage<OnStatusCommandMessage>();
        RegisterMessage<PauseCommandMessage>();
        RegisterMessage<Play2CommandMessage>();
        RegisterMessage<PlayCommandMessage>();
        RegisterMessage<PublishCommandMessage>();
        RegisterMessage<ReceiveAudioCommandMessage>();
        RegisterMessage<ReceiveVideoCommandMessage>();
        RegisterMessage<SeekCommandMessage>();
    }

    public void RegisterMessage<T>() where T: CommandMessage
    {
        var tType = typeof(T);
        var attr = tType.GetCustomAttribute<RtmpCommandAttribute>();
        if (attr == null)
            throw new InvalidOperationException();
        MessageFactories.Add(attr.Name, tType);
    }

    public Message Provide(MessageHeader header, SerializationContext context, out int consumed)
    {
        string name;
        var amf3 = false;
        switch (header.MessageType)
        {
            case MessageType.Amf0Command:
            {
                if (!context.Amf0Reader.TryGetString(context.ReadBuffer.Span, out name, out consumed))
                    throw new ProtocolViolationException();
                break;
            }
            case MessageType.Amf3Command:
            {
                amf3 = true;
                if (!context.Amf3Reader.TryGetString(context.ReadBuffer.Span, out name, out consumed))
                    throw new ProtocolViolationException();
                break;
            }
            default:
                throw new InvalidOperationException();
        }
        if (!MessageFactories.TryGetValue(name, out var t))
            throw new NotSupportedException();
        var ret = (CommandMessage)Activator.CreateInstance(t, amf3 ? AmfEncodingVersion.Amf3 : AmfEncodingVersion.Amf0);
        if (ret == null)
            throw new InvalidOperationException();
        ret.ProcedureName = name;
        return ret;
    }
}