using SharpRtmp.Networking.Rtmp.Data;
using SharpRtmp.Networking.Rtmp.Serialization;

namespace SharpRtmp.Networking.Rtmp.Messages.UserControlMessages;

public enum UserControlEventType : ushort
{
    StreamBegin,
    StreamEof,
    StreamDry,
    SetBufferLength,
    StreamIsRecorded,
    PingRequest,
    PingResponse
}

[RtmpMessage(MessageType.UserControlMessages)]
public abstract class UserControlMessage : ControlMessage
{
    public UserControlEventType UserControlEventType { get; set; }
}