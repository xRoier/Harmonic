using SharpRtmp.Networking.Rtmp.Serialization;

namespace SharpRtmp.Networking.Rtmp.Messages.Commands;

[RtmpCommand(Name = "createStream")]
public class CreateStreamCommandMessage : CommandMessage
{
    public CreateStreamCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
    }
}