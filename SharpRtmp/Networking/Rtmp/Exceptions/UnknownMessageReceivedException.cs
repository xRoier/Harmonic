using System;
using SharpRtmp.Networking.Rtmp.Data;

namespace SharpRtmp.Networking.Rtmp.Exceptions;

public class UnknownMessageReceivedException : Exception
{
    public MessageHeader Header { get; set; }

    public UnknownMessageReceivedException(MessageHeader header) => Header = header;
}