using System.Collections.Generic;

namespace SharpRtmp.Networking.Amf.Data;

public interface IDynamicObject
{
    IReadOnlyDictionary<string, object> DynamicFields { get; }

    void AddDynamic(string key, object data);
}