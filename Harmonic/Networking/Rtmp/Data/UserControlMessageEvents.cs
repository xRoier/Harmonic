namespace Harmonic.Networking.Rtmp.Data;

public enum UserControlMessageEvents : ushort
{
    StreamBegin,
    StreamEof,
    StreamDry,
    SetBufferLength,
    StreamIsRecorded,
    PingRequest,
    PingResponse
}