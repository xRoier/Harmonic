using SharpRtmp.Networking.Rtmp.Serialization;

namespace SharpRtmp.Networking.Rtmp.Messages.Commands;

[RtmpCommand(Name = "deleteStream")]
public class DeleteStreamCommandMessage : CommandMessage
{
    [OptionalArgument]
    public double StreamId { get; set; }

    public DeleteStreamCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
    }
}