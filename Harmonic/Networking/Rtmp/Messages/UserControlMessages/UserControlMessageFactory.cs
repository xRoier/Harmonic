﻿using Harmonic.Networking.Rtmp.Data;
using Harmonic.Networking.Rtmp.Serialization;
using Harmonic.Networking.Utils;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace Harmonic.Networking.Rtmp.Messages.UserControlMessages;

public class UserControlMessageFactory
{
    public readonly Dictionary<UserControlEventType, Type> MessageFactories = new();

    public void RegisterMessage<T>() where T: UserControlMessage, new()
    {
        var tType = typeof(T);
        var attr = tType.GetCustomAttribute<UserControlMessageAttribute>();
        if (attr == null)
            throw new InvalidOperationException();
        MessageFactories.Add(attr.Type, tType);
    }

    public Message Provide(MessageHeader header, SerializationContext context, out int consumed)
    {
        var type = (UserControlEventType)NetworkBitConverter.ToUInt16(context.ReadBuffer.Span);
        if (!MessageFactories.TryGetValue(type, out var t))
            throw new NotSupportedException();
        consumed = 0;
        return (Message)Activator.CreateInstance(t);
    }
}