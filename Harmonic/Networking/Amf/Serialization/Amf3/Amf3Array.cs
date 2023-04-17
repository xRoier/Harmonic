using System.Collections.Generic;

namespace Harmonic.Networking.Amf.Serialization.Amf3;

public class Amf3Array
{
    public Dictionary<string, object> SparsePart { get; set; } = new();
    public List<object> DensePart { get; set; } = new();

    public object this[string key]
    {
        get => SparsePart[key];
        set => SparsePart[key] = value;
    }

    public object this[int index]
    {
        get => DensePart[index];
        set => DensePart[index] = value;
    }
}