using System;
using System.Collections.Generic;
using System.Linq;
using SharpRtmp.Networking.Rtmp.Data;

namespace SharpRtmp.Networking.Rtmp.Serialization;

[AttributeUsage(AttributeTargets.Class)]
public class RtmpMessageAttribute : Attribute
{
    public RtmpMessageAttribute(params MessageType[] messageTypes)
    {
        MessageTypes = messageTypes.ToList();
    }

    internal List<MessageType> MessageTypes { get; set; }
}