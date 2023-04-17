using System;
using SharpRtmp.Networking.Rtmp.Messages.UserControlMessages;

namespace SharpRtmp.Networking.Rtmp.Serialization;

public class UserControlMessageAttribute : Attribute
{
    public UserControlEventType Type { get; set; }
}