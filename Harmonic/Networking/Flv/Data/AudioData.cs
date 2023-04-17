using System;

namespace Harmonic.Networking.Flv.Data;

public class AudioData
{
    public AacPacketType? AacPacketType { get; set; }
    public ReadOnlyMemory<byte> Data { get; set; }
}