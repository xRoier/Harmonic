using SharpRtmp.Networking.Rtmp.Serialization;

namespace SharpRtmp.Networking.Rtmp.Messages.Commands;

[RtmpCommand(Name = "pause")]
public class PauseCommandMessage : CommandMessage
{
    [OptionalArgument]
    public bool IsPause { get; set; }
    [OptionalArgument]
    public double MilliSeconds { get; set; }

    public PauseCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
    }
}