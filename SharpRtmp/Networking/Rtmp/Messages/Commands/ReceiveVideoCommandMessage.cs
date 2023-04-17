using SharpRtmp.Networking.Rtmp.Serialization;

namespace SharpRtmp.Networking.Rtmp.Messages.Commands;

[RtmpCommand(Name = "receiveVideo")]
public class ReceiveVideoCommandMessage : CommandMessage
{
    [OptionalArgument]
    public bool IsReceive { get; set; }

    public ReceiveVideoCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
    }
}