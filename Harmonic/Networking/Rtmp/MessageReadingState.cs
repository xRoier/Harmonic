namespace Harmonic.Networking.Rtmp;

class MessageReadingState
{
    public uint MessageLength;
    public byte[] Body;
    public int CurrentIndex;
    public long RemainBytes => MessageLength - CurrentIndex;
    public bool IsCompleted => RemainBytes == 0;
}