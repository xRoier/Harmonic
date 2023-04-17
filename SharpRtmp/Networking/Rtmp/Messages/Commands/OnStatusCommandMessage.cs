using SharpRtmp.Networking.Rtmp.Serialization;

namespace SharpRtmp.Networking.Rtmp.Messages.Commands;

[RtmpCommand(Name = "onStatus")]
public class OnStatusCommandMessage : CommandMessage
{
    [OptionalArgument]
    public object InfoObject { get; set; }

    public OnStatusCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
    }
}