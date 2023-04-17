using System;

namespace SharpRtmp.Networking.Rtmp.Data;

public class SharedObjectMessage
{
    public string SharedObjectName { get; set; }
    public ushort CurrentVersion { get; set; }
    // TBD
}