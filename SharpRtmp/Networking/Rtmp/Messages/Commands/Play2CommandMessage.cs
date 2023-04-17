using SharpRtmp.Networking.Rtmp.Serialization;

namespace SharpRtmp.Networking.Rtmp.Messages.Commands;

[RtmpCommand(Name = "play2")]
public class Play2CommandMessage : CommandMessage
{
    [OptionalArgument]
    public object Parameters { get; set; }

    public Play2CommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
    }
}