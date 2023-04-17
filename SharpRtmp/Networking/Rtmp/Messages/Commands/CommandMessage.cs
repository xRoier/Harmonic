using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using SharpRtmp.Networking.Amf.Common;
using SharpRtmp.Networking.Rtmp.Data;
using SharpRtmp.Networking.Rtmp.Serialization;

namespace SharpRtmp.Networking.Rtmp.Messages.Commands;

[RtmpMessage(MessageType.Amf3Command, MessageType.Amf0Command)]
public abstract class CommandMessage : Message
{
    public CommandMessage(AmfEncodingVersion encoding)
    {
        AmfEncodingVersion = encoding;
        MessageHeader.MessageType =
            encoding == AmfEncodingVersion.Amf0 ? MessageType.Amf0Command : MessageType.Amf3Command;
    }

    public AmfEncodingVersion AmfEncodingVersion { get; set; }
    public virtual string ProcedureName { get; set; }
    public double TransactionId { get; set; }
    public virtual AmfObject CommandObject { get; set; }

    public void DeserializeAmf0(SerializationContext context)
    {
        var buffer = context.ReadBuffer.Span;
        if (!context.Amf0Reader.TryGetNumber(buffer, out var txid, out var consumed))
            throw new InvalidOperationException();

        TransactionId = txid;
        buffer = buffer[consumed..];
        context.Amf0Reader.TryGetObject(buffer, out var commandObj, out consumed);
        CommandObject = commandObj;
        buffer = buffer[consumed..];
        var optionArguments = GetType().GetProperties()
            .Where(p => p.GetCustomAttribute<OptionalArgumentAttribute>() != null).ToList();
        var i = 0;
        while (buffer.Length > 0)
        {
            if (!context.Amf0Reader.TryGetValue(buffer, out _, out var optArg, out consumed))
                break;
            buffer = buffer[consumed..];
            optionArguments[i].SetValue(this, optArg);
            i++;
            if (i >= optionArguments.Count)
                break;
        }
    }

    public void DeserializeAmf3(SerializationContext context)
    {
        var buffer = context.ReadBuffer.Span;
        if (!context.Amf3Reader.TryGetDouble(buffer, out var txid, out var consumed))
            throw new InvalidOperationException();
        TransactionId = txid;
        buffer = buffer.Slice(consumed);
        context.Amf3Reader.TryGetObject(buffer, out var commandObj, out consumed);
        CommandObject = commandObj as AmfObject;
        buffer = buffer.Slice(consumed);
        var optionArguments = GetType().GetProperties()
            .Where(p => p.GetCustomAttribute<OptionalArgumentAttribute>() != null).ToList();
        var i = 0;
        while (buffer.Length > 0)
        {
            context.Amf0Reader.TryGetValue(buffer, out _, out var optArg, out _);
            optionArguments[i].SetValue(this, optArg);
        }
    }

    public void SerializeAmf0(SerializationContext context)
    {
        using var writeContext = new Amf.Serialization.Amf0.SerializationContext(context.WriteBuffer);
        ProcedureName ??= GetType().GetCustomAttribute<RtmpCommandAttribute>()?.Name;
        Debug.Assert(!string.IsNullOrEmpty(ProcedureName));
        context.Amf0Writer.WriteBytes(ProcedureName, writeContext);
        context.Amf0Writer.WriteBytes(TransactionId, writeContext);
        context.Amf0Writer.WriteValueBytes(CommandObject, writeContext);
        var optionArguments = GetType().GetProperties()
            .Where(p => p.GetCustomAttribute<OptionalArgumentAttribute>() != null).ToList();
        foreach (var optionArgument in optionArguments)
            context.Amf0Writer.WriteValueBytes(optionArgument.GetValue(this), writeContext);
    }

    public void SerializeAmf3(SerializationContext context)
    {
        using var writeContext = new Amf.Serialization.Amf3.SerializationContext(context.WriteBuffer);
        ProcedureName ??= GetType().GetCustomAttribute<RtmpCommandAttribute>()?.Name;
        Debug.Assert(!string.IsNullOrEmpty(ProcedureName));
        context.Amf3Writer.WriteBytes(ProcedureName, writeContext);
        context.Amf3Writer.WriteBytes(TransactionId, writeContext);
        context.Amf3Writer.WriteValueBytes(CommandObject, writeContext);
        var optionArguments = GetType().GetProperties()
            .Where(p => p.GetCustomAttribute<OptionalArgumentAttribute>() != null).ToList();
        foreach (var optionArgument in optionArguments)
            context.Amf3Writer.WriteValueBytes(optionArgument.GetValue(this), writeContext);
    }

    public sealed override void Deserialize(SerializationContext context)
    {
        if (AmfEncodingVersion == AmfEncodingVersion.Amf0)
            DeserializeAmf0(context);
        else
            DeserializeAmf3(context);
    }

    public sealed override void Serialize(SerializationContext context)
    {
        if (AmfEncodingVersion == AmfEncodingVersion.Amf0)
            SerializeAmf0(context);
        else
            SerializeAmf3(context);
    }
}