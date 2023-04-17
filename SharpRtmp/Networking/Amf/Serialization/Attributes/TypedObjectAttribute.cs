using System;

namespace SharpRtmp.Networking.Amf.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class TypedObjectAttribute : Attribute
{
    public string Name { get; set; }
}