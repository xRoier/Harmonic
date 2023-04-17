using System;

namespace Harmonic.Networking.Amf.Serialization.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class TypedObjectAttribute : Attribute
{
    public string Name { get; set; }
}