using System;

namespace SharpRtmp.Rpc;

[AttributeUsage(AttributeTargets.Parameter)]
public class FromCommandObjectAttribute : Attribute
{
    public FromCommandObjectAttribute(string key = null)
    {
        Key = key;
    }
    public string Key { get; set; } = null;
}