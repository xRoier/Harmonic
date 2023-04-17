using SharpRtmp.Networking.Rtmp.Serialization;

namespace SharpRtmp.Networking.Rtmp.Messages.Commands;

[RtmpCommand(Name = "connect")]
public class ConnectCommandMessage : CommandMessage
{
    [OptionalArgument]
    public object UserArguments { get; set; }

    public ConnectCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
    }
}