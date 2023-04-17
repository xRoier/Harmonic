namespace SharpRtmp.Networking.Rtmp.Streaming;

public enum PublishingType
{
    [PublishingTypeName("")]
    None,
    [PublishingTypeName("live")]
    Live,
    [PublishingTypeName("record")]
    Record,
    [PublishingTypeName("append")]
    Append
}