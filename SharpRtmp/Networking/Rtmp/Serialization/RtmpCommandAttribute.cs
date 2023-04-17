using System;

namespace SharpRtmp.Networking.Rtmp.Serialization;

[AttributeUsage(AttributeTargets.Class)]
public class RtmpCommandAttribute : Attribute
{
    public string Name { get; set; }
}