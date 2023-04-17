using Harmonic.Networking.Amf.Data;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Harmonic.Networking.Amf.Common;

public class AmfObject : IDynamicObject, IEnumerable
{
    private readonly Dictionary<string, object> _fields = new();

    private readonly Dictionary<string, object> _dynamicFields = new();

    public bool IsAnonymous => GetType() == typeof(AmfObject);
    public bool IsDynamic => _dynamicFields.Any();

    public IReadOnlyDictionary<string, object> DynamicFields => _dynamicFields;

    public IReadOnlyDictionary<string, object> Fields => _fields;

    public AmfObject()
    {
    }

    public AmfObject(Dictionary<string, object> values) => _fields = values;

    public void Add(string memberName, object member) => _fields.Add(memberName, member);

    public void AddDynamic(string memberName, object member) => _dynamicFields.Add(memberName, member);

    public IEnumerator GetEnumerator() => ((IEnumerable)Fields).GetEnumerator();
}