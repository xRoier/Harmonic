using System;

namespace SharpRtmp.Networking.Amf.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public class ClassFieldAttribute : Attribute
{
    public string Name { get; set; }
}