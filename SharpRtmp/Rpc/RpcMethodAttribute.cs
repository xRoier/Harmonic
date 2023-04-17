using System;

namespace SharpRtmp.Rpc;

public class RpcMethodAttribute : Attribute
{
    public string Name { get; set; }
    public RpcMethodAttribute(string name = null)
    {
        Name = name;
    }
}