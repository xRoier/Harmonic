namespace SharpRtmp.Networking.Rtmp.Data;

class ChunkHeader
{
    public ChunkBasicHeader ChunkBasicHeader { get; set; }
    public MessageHeader MessageHeader { get; set; }
    public uint ExtendedTimestamp { get; set; }
}