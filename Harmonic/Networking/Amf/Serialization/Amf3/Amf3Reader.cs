﻿using Harmonic.Networking.Amf.Common;
using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Xml;
using Harmonic.Networking.Utils;
using System.Buffers;
using Harmonic.Networking.Amf.Data;
using System.Reflection;
using Harmonic.Networking.Amf.Serialization.Attributes;

namespace Harmonic.Networking.Amf.Serialization.Amf3;

public class Amf3Reader
{
    private delegate bool ReaderHandler<T>(Span<byte> buffer, out T value, out int consumed);
    private delegate bool ReaderHandler(Span<byte> buffer, out object value, out int consumed);

    private readonly List<object> _objectReferenceTable = new();
    private readonly List<string> _stringReferenceTable = new();
    private readonly List<Amf3ClassTraits> _objectTraitsReferenceTable = new();
    private readonly Dictionary<Amf3Type, ReaderHandler> _readerHandlers;
    private readonly Dictionary<string, TypeRegisterState> _registeredTypedObejectStates = new();
    private readonly List<Type> _registeredTypes = new();
    private readonly Dictionary<string, Type> _registeredExternalizable = new();
    private readonly IReadOnlyList<Amf3Type> _supportedTypes;
    private readonly MemoryPool<byte> _memoryPool = MemoryPool<byte>.Shared;

    public IReadOnlyList<Type> RegisteredTypes => _registeredTypes;

    public Amf3Reader()
    {
        var supportedTypes = new List<Amf3Type>
        {
            Amf3Type.Undefined ,
            Amf3Type.Null ,
            Amf3Type.False ,
            Amf3Type.True,
            Amf3Type.Integer ,
            Amf3Type.Double ,
            Amf3Type.String ,
            Amf3Type.Xml ,
            Amf3Type.XmlDocument ,
            Amf3Type.Date ,
            Amf3Type.Array ,
            Amf3Type.Object ,
            Amf3Type.ByteArray ,
            Amf3Type.VectorObject ,
            Amf3Type.VectorDouble ,
            Amf3Type.VectorInt ,
            Amf3Type.VectorUInt ,
            Amf3Type.Dictionary
        };
        _supportedTypes = supportedTypes;

        var readerHandlers = new Dictionary<Amf3Type, ReaderHandler>
        {
            [Amf3Type.Undefined] = ReaderHandlerWrapper<Undefined>(TryGetUndefined),
            [Amf3Type.Null] = ReaderHandlerWrapper<object>(TryGetNull),
            [Amf3Type.True] = ReaderHandlerWrapper<bool>(TryGetTrue),
            [Amf3Type.False] = ReaderHandlerWrapper<bool>(TryGetFalse),
            [Amf3Type.Double] = ReaderHandlerWrapper<double>(TryGetDouble),
            [Amf3Type.Integer] = ReaderHandlerWrapper<uint>(TryGetUInt29),
            [Amf3Type.String] = ReaderHandlerWrapper<string>(TryGetString),
            [Amf3Type.Xml] = ReaderHandlerWrapper<Amf3Xml>(TryGetXml),
            [Amf3Type.XmlDocument] = ReaderHandlerWrapper<XmlDocument>(TryGetXmlDocument),
            [Amf3Type.Date] = ReaderHandlerWrapper<DateTime>(TryGetDate),
            [Amf3Type.ByteArray] = ReaderHandlerWrapper<byte[]>(TryGetByteArray),
            [Amf3Type.VectorDouble] = ReaderHandlerWrapper<Vector<double>>(TryGetVectorDouble),
            [Amf3Type.VectorInt] = ReaderHandlerWrapper<Vector<int>>(TryGetVectorInt),
            [Amf3Type.VectorUInt] = ReaderHandlerWrapper<Vector<uint>>(TryGetVectorUint),
            [Amf3Type.VectorObject] = ReaderHandlerWrapper<object>(TryGetVectorObject),
            [Amf3Type.Array] = ReaderHandlerWrapper<Amf3Array>(TryGetArray),
            [Amf3Type.Object] = ReaderHandlerWrapper<object>(TryGetObject),
            [Amf3Type.Dictionary] = ReaderHandlerWrapper<Amf3Dictionary<object, object>>(TryGetDictionary)
        };
        _readerHandlers = readerHandlers;
    }

    public void ResetReference()
    {
        _objectReferenceTable.Clear();
        _objectTraitsReferenceTable.Clear();
        _stringReferenceTable.Clear();
    }

    private ReaderHandler ReaderHandlerWrapper<T>(ReaderHandler<T> handler)
    {
        return (Span<byte> b, out object value, out int consumed) =>
        {
            value = default;
            consumed = default;

            if (!handler(b, out var data, out consumed))
                return false;
            value = data;
            return true;
        };
    }

    internal void RegisterTypedObject(string mappedName, TypeRegisterState state) => _registeredTypedObejectStates.Add(mappedName, state);

    public void RegisterTypedObject<T>() where T: new()
    {
        var type = typeof(T);
        var props = type.GetProperties();
        var fields = props.Where(p => p.CanWrite && Attribute.GetCustomAttribute(p, typeof(ClassFieldAttribute)) != null).ToList();
        var members = fields.ToDictionary(p => ((ClassFieldAttribute)Attribute.GetCustomAttribute(p, typeof(ClassFieldAttribute)))?.Name ?? p.Name, p => new Action<object, object>(p.SetValue));
        if (members.Keys.Any(string.IsNullOrEmpty))
            throw new InvalidOperationException("Field name cannot be empty or null");
        string mapedName = null;
        var attr = type.GetCustomAttribute<TypedObjectAttribute>();
        if (attr != null)
            mapedName = attr.Name;

        var typeName = mapedName ?? type.Name;
        var state = new TypeRegisterState
        {
            Members = members,
            Type = type
        };
        _registeredTypes.Add(type);
        _registeredTypedObejectStates.Add(typeName, state);
    }

    public void RegisterExternalizable<T>() where T : IExternalizable, new()
    {
        var type = typeof(T);
        string mapedName = null;
        var attr = type.GetCustomAttribute<TypedObjectAttribute>();
        if (attr != null)
            mapedName = attr.Name;
        var typeName = mapedName ?? type.Name;
        _registeredExternalizable.Add(typeName, type);
    }

    public bool TryDescribeData(Span<byte> buffer, out Amf3Type type)
    {
        type = default;
        if (buffer.Length < Amf3CommonValues.MarkerLength)
            return false;

        var typeMark = (Amf3Type)buffer[0];
        if (!_supportedTypes.Contains(typeMark))
            return false;

        type = typeMark;
        return true;
    }

    public bool DataIsType(Span<byte> buffer, Amf3Type type)
    {
        if (!TryDescribeData(buffer, out var dataType))
            return false;
        return dataType == type;
    }

    public bool TryGetUndefined(Span<byte> buffer, out Undefined value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.Undefined))
            return false;

        value = new Undefined();
        consumed = Amf3CommonValues.MarkerLength;
        return true;
    }

    public bool TryGetNull(Span<byte> buffer, out object value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.Null))
            return false;

        value = null;
        consumed = Amf3CommonValues.MarkerLength;
        return true;
    }

    public bool TryGetTrue(Span<byte> buffer, out bool value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.True))
            return false;

        value = true;
        consumed = Amf3CommonValues.MarkerLength;
        return true;
    }

    public bool TryGetBoolean(Span<byte> buffer, out bool value, out int consumed)
    {
        if (DataIsType(buffer, Amf3Type.True))
        {
            consumed = Amf3CommonValues.MarkerLength;
            value = true;
            return true;
        }

        if (DataIsType(buffer, Amf3Type.False))
        {
            consumed = Amf3CommonValues.MarkerLength;
            value = false;
            return true;
        }
        value = default;
        consumed = default;
        return false;
    }

    public bool TryGetFalse(Span<byte> buffer, out bool value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.False))
            return false;

        value = false;
        consumed = Amf3CommonValues.MarkerLength;
        return true;
    }

    public bool TryGetUInt29(Span<byte> buffer, out uint value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.Integer))
            return false;

        var dataBuffer = buffer[Amf3CommonValues.MarkerLength..];

        if (!TryGetU29Impl(dataBuffer, out value, out var dataConsumed))
            return false;

        consumed = Amf3CommonValues.MarkerLength + dataConsumed;

        return true;
    }

    private bool TryGetU29Impl(Span<byte> dataBuffer, out uint value, out int consumed)
    {
        value = default;
        consumed = default;
        var bytesNeed = 0;
        for (var i = 0; i < 4; i++)
        {
            bytesNeed++;
            if (dataBuffer.Length < bytesNeed)
                return false;
            var hasBytes = (dataBuffer[i] >> 7 & 0x01) == 0x01;
            if (!hasBytes)
                break;
        }

        switch (bytesNeed)
        {
            case 3:
            case 4:
                dataBuffer[2] = (byte)(0x7F & dataBuffer[2]);
                dataBuffer[1] = (byte)(0x7F & dataBuffer[1]);
                dataBuffer[0] = (byte)(0x7F & dataBuffer[0]);
                dataBuffer[2] = (byte)(dataBuffer[1] << 7 | dataBuffer[2]);
                dataBuffer[1] = (byte)(dataBuffer[0] << 6 | (dataBuffer[1] >> 1));
                dataBuffer[0] = (byte)(dataBuffer[0] >> 2);
                break;
            case 2:
                dataBuffer[1] = (byte)(0x7F & dataBuffer[1]);
                dataBuffer[1] = (byte)(dataBuffer[0] << 7 | dataBuffer[1]);
                dataBuffer[0] = (byte)(0x7F & dataBuffer[0]);
                dataBuffer[0] = (byte)(dataBuffer[0] >> 1);
                break;
        }

        using var mem = _memoryPool.Rent(sizeof(uint));
        var buffer = mem.Memory.Span;
        buffer.Clear();
        dataBuffer[..bytesNeed].CopyTo(buffer[(sizeof(uint) - bytesNeed)..]);
        value = NetworkBitConverter.ToUInt32(buffer);
        consumed = bytesNeed;
        return true;
    }

    public bool TryGetDouble(Span<byte> buffer, out double value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.Double))
            return false;

        value = NetworkBitConverter.ToDouble(buffer[Amf3CommonValues.MarkerLength..]);
        consumed = Amf3CommonValues.MarkerLength + sizeof(double);
        return true;
    }
    public bool TryGetString(Span<byte> buffer, out string value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.String))
            return false;

        var objectBuffer = buffer[Amf3CommonValues.MarkerLength..];
        TryGetStringImpl(objectBuffer, _stringReferenceTable, out var str, out var strConsumed);
        value = str;
        consumed = Amf3CommonValues.MarkerLength + strConsumed;
        return true;
    }

    private bool TryGetStringImpl<T>(Span<byte> objectBuffer, List<T> referenceTable, out string value, out int consumed) where T : class
    {
        value = default;
        consumed = default;
        if (!TryGetU29Impl(objectBuffer, out var header, out var headerLen))
            return false;
        if (!TryGetReference(header, _stringReferenceTable, out var headerData, out string refValue, out var isRef))
            return false;
        if (isRef)
        {
            value = refValue;
            consumed = headerLen;
            return true;
        }

        var strLen = (int)headerData;
        if (objectBuffer.Length - headerLen < strLen)
            return false;

        value = Encoding.UTF8.GetString(objectBuffer.Slice(headerLen, strLen));
        consumed = headerLen + strLen;
        if (value.Any())
            referenceTable.Add(value as T);

        return true;
    }

    public bool TryGetXmlDocument(Span<byte> buffer, out XmlDocument value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.XmlDocument))
            return false;

        var objectBuffer = buffer[Amf3CommonValues.MarkerLength..];
        TryGetStringImpl(objectBuffer, _objectReferenceTable, out var str, out var strConsumed);
        var xml = new XmlDocument();
        xml.LoadXml(str);
        value = xml;
        consumed = Amf3CommonValues.MarkerLength + strConsumed;
        return true;
    }

    public bool TryGetDate(Span<byte> buffer, out DateTime value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.Date))
            return false;

        var objectBuffer = buffer[Amf3CommonValues.MarkerLength..];

        if (!TryGetU29Impl(objectBuffer, out var header, out var headerLength))
            return false;
        if (!TryGetReference(header, _objectReferenceTable, out _, out DateTime refValue, out var isRef))
            return false;
        if (isRef)
        {
            value = refValue;
            consumed = Amf3CommonValues.MarkerLength + headerLength;
            return true;
        }

        var timestamp = NetworkBitConverter.ToDouble(objectBuffer[headerLength..]);
        value = DateTimeOffset.FromUnixTimeMilliseconds((long)timestamp).LocalDateTime;
        consumed = Amf3CommonValues.MarkerLength + headerLength + sizeof(double);
        _objectReferenceTable.Add(value);
        return true;
    }

    public bool TryGetArray(Span<byte> buffer, out Amf3Array value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.Array))
            return false;

        if (!TryGetU29Impl(buffer[Amf3CommonValues.MarkerLength..], out var header, out var headerConsumed))
            return false;

        if (!TryGetReference(header, _objectReferenceTable, out var headerData, out Amf3Array refValue, out var isRef))
            return false;
        if (isRef)
        {
            value = refValue;
            consumed = Amf3CommonValues.MarkerLength + headerConsumed;
            return true;
        }

        var arrayConsumed = 0;
        var arrayBuffer = buffer[(Amf3CommonValues.MarkerLength + headerConsumed)..];
        var denseItemCount = (int)headerData;

        if (!TryGetStringImpl(arrayBuffer, _stringReferenceTable, out var key, out var keyConsumed))
            return false;
        var array = new Amf3Array();
        _objectReferenceTable.Add(array);
        while (key.Any())
        {
            arrayBuffer = arrayBuffer[keyConsumed..];
            arrayConsumed += keyConsumed;
            if (!TryGetValue(arrayBuffer, out var item, out var itemConsumed))
                return false;

            arrayConsumed += itemConsumed;
            arrayBuffer = arrayBuffer[itemConsumed..];
            array.SparsePart.Add(key, item);
            if (!TryGetStringImpl(arrayBuffer, _stringReferenceTable, out key, out keyConsumed))
                return false;
        }
        arrayConsumed += keyConsumed;
        arrayBuffer = arrayBuffer[keyConsumed..];

        for (var i = 0; i < denseItemCount; i++)
        {
            if (!TryGetValue(arrayBuffer, out var item, out var itemConsumed))
                return false;
            array.DensePart.Add(item);
            arrayConsumed += itemConsumed;
            arrayBuffer = arrayBuffer[itemConsumed..];
        }

        value = array;
        consumed = Amf3CommonValues.MarkerLength + headerConsumed + arrayConsumed;
        return true;
    }

    public bool TryGetObject(Span<byte> buffer, out object value, out int consumed)
    {
        value = default;
        consumed = 0;
        if (!DataIsType(buffer, Amf3Type.Object))
            return false;
        consumed = Amf3CommonValues.MarkerLength;
        if (!TryGetU29Impl(buffer[Amf3CommonValues.MarkerLength..], out var header, out var headerLength))
            return false;
        consumed += headerLength;

        if (!TryGetReference(header, _objectReferenceTable, out _, out object refValue, out var isRef))
            return false;

        if (isRef)
        {
            value = refValue;
            return true;
        }
        Amf3ClassTraits traits;
        if ((header & 0x02) == 0x00)
        {
            var referenceIndex = (int)((header >> 2) & 0x3FFFFFFF);
            if (_objectTraitsReferenceTable.Count <= referenceIndex)
                return false;

            if (_objectTraitsReferenceTable[referenceIndex] is { } obj)
                traits = obj;
            else
                return false;
        }
        else
        {
            traits = new Amf3ClassTraits();
            var dataBuffer = buffer[(Amf3CommonValues.MarkerLength + headerLength)..];
            if ((header & 0x04) == 0x04)
            {
                traits.ClassType = Amf3ClassType.Externalizable;
                if (!TryGetStringImpl(dataBuffer, _stringReferenceTable, out var extClassName, out var extClassNameConsumed))
                    return false;
                consumed += extClassNameConsumed;
                traits.ClassName = extClassName;
                var externailzableBuffer = dataBuffer[extClassNameConsumed..];

                if (!_registeredExternalizable.TryGetValue(extClassName, out var extType))
                    return false;
                if(Activator.CreateInstance(extType) is not IExternalizable extObj)
                    return false;
                if (!extObj.TryDecodeData(externailzableBuffer, out var extConsumed))
                    return false;

                value = extObj;
                consumed = Amf3CommonValues.MarkerLength + headerLength + extClassNameConsumed + extConsumed;
                return true;
            }

            if (!TryGetStringImpl(dataBuffer, _stringReferenceTable, out var className, out var classNameConsumed))
                return false;
            dataBuffer = dataBuffer[classNameConsumed..];
            consumed += classNameConsumed;
            if (className.Any())
            {
                traits.ClassType = Amf3ClassType.Typed;
                traits.ClassName = className;
            }
            else
                traits.ClassType = Amf3ClassType.Anonymous;

            if ((header & 0x08) == 0x08) traits.IsDynamic = true;
            var memberCount = header >> 4;
            for (var i = 0; i < memberCount; i++)
            {
                if (!TryGetStringImpl(dataBuffer, _stringReferenceTable, out var key, out var keyConsumed))
                    return false;
                traits.Members.Add(key);
                dataBuffer = dataBuffer[keyConsumed..];
                consumed += keyConsumed;
            }
            _objectTraitsReferenceTable.Add(traits);
        }

        object deserailziedObject;
        var valueBuffer = buffer[consumed..];
        if (traits.ClassType == Amf3ClassType.Typed)
        {
            if (!_registeredTypedObejectStates.TryGetValue(traits.ClassName, out var state))
                return false;

            var classType = state.Type;
            if (!traits.Members.OrderBy(m => m).SequenceEqual(state.Members.Keys.OrderBy(p => p)))
                return false;

            deserailziedObject = Activator.CreateInstance(classType);
            _objectReferenceTable.Add(deserailziedObject);
            foreach (var member in traits.Members)
            {
                if (!TryGetValue(valueBuffer, out var data, out var dataConsumed))
                    return false;
                valueBuffer = valueBuffer[dataConsumed..];
                consumed += dataConsumed;
                state.Members[member](deserailziedObject, data);
            }
        }
        else
        {
            var obj = new AmfObject();
            _objectReferenceTable.Add(obj);
            foreach (var member in traits.Members)
            {
                if (!TryGetValue(valueBuffer, out var data, out var dataConsumed))
                    return false;
                valueBuffer = valueBuffer[dataConsumed..];
                consumed += dataConsumed;
                obj.Add(member, data);
            }

            deserailziedObject = obj;
        }
        if (traits.IsDynamic)
        {
            if (deserailziedObject is not IDynamicObject dynamicObject)
                return false;
            if (!TryGetStringImpl(valueBuffer, _stringReferenceTable, out var key, out var keyConsumed))
                return false;
            consumed += keyConsumed;
            valueBuffer = valueBuffer[keyConsumed..];
            while (key.Any())
            {
                if (!TryGetValue(valueBuffer, out var data, out var dataConsumed))
                    return false;
                valueBuffer = valueBuffer[dataConsumed..];
                consumed += dataConsumed;

                dynamicObject.AddDynamic(key, data);

                if (!TryGetStringImpl(valueBuffer, _stringReferenceTable, out key, out keyConsumed))
                {
                    return false;
                }
                valueBuffer = valueBuffer[keyConsumed..];
                consumed += keyConsumed;
            }
        }

        value = deserailziedObject;
        return true;
    }

    public bool TryGetXml(Span<byte> buffer, out Amf3Xml value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.Xml))
            return false;

        var objectBuffer = buffer[Amf3CommonValues.MarkerLength..];
        TryGetStringImpl(objectBuffer, _objectReferenceTable, out var str, out var strConsumed);
        var xml = new Amf3Xml();
        xml.LoadXml(str);

        value = xml;
        consumed = Amf3CommonValues.MarkerLength + strConsumed;
        return true;
    }

    public bool TryGetByteArray(Span<byte> buffer, out byte[] value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.ByteArray))
            return false;

        var objectBuffer = buffer[Amf3CommonValues.MarkerLength..];

        if (!TryGetU29Impl(objectBuffer, out var header, out var headerLen))
            return false;

        if (!TryGetReference(header, _objectReferenceTable, out var headerData, out byte[] refValue, out var isRef))
            return false;
        if (isRef)
        {
            value = refValue;
            consumed = Amf3CommonValues.MarkerLength + headerLen;
            return true;
        }

        var arrayLen = (int)headerData;
        if (objectBuffer.Length - headerLen < arrayLen)
            return false;

        value = new byte[arrayLen];

        objectBuffer.Slice(headerLen, arrayLen).CopyTo(value);
        _objectReferenceTable.Add(value);
        consumed = Amf3CommonValues.MarkerLength + headerLen + arrayLen;
        return true;
    }

    public bool TryGetValue(Span<byte> buffer, out object value, out int consumed)
    {
        value = default;
        consumed = default;

        return TryDescribeData(buffer, out var type) && _readerHandlers.TryGetValue(type, out var handler) &&
               handler(buffer, out value, out consumed);
    }

    public bool TryGetVectorInt(Span<byte> buffer, out Vector<int> value, out int consumed)
    {
        value = default;
        consumed = Amf3CommonValues.MarkerLength;
        if (!DataIsType(buffer, Amf3Type.VectorInt))
            return false;

        buffer = buffer[Amf3CommonValues.MarkerLength..];
        if (!ReadVectorHeader(ref buffer, ref value, ref consumed, out var itemCount, out var isFixedSize, out var isRef))
            return false;

        if (isRef)
            return true;

        var vector = new Vector<int> { IsFixedSize = isFixedSize };
        _objectReferenceTable.Add(vector);
        for (var i = 0; i < itemCount; i++)
        {
            if (!TryGetIntVectorData(ref buffer, vector, ref consumed))
                return false;
        }
        value = vector;
        return true;
    }

    public bool TryGetVectorUint(Span<byte> buffer, out Vector<uint> value, out int consumed)
    {
        value = default;
        consumed = Amf3CommonValues.MarkerLength;
        if (!DataIsType(buffer, Amf3Type.VectorUInt))
            return false;

        buffer = buffer[Amf3CommonValues.MarkerLength..];
        if (!ReadVectorHeader(ref buffer, ref value, ref consumed, out var itemCount, out var isFixedSize, out var isRef))
            return false;

        if (isRef)
            return true;

        var vector = new Vector<uint> { IsFixedSize = isFixedSize };
        _objectReferenceTable.Add(vector);
        for (var i = 0; i < itemCount; i++)
        {
            if (!TryGetUIntVectorData(ref buffer, vector, ref consumed))
                return false;
        }

        value = vector;
        return true;
    }

    public bool TryGetVectorDouble(Span<byte> buffer, out Vector<double> value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.VectorDouble))
            return false;

        buffer = buffer[Amf3CommonValues.MarkerLength..];
        if (!ReadVectorHeader(ref buffer, ref value, ref consumed, out var itemCount, out var isFixedSize, out var isRef))
            return false;

        if (isRef)
            return true;

        var vector = new Vector<double> { IsFixedSize = isFixedSize };
        _objectReferenceTable.Add(vector);
        for (var i = 0; i < itemCount; i++)
        {
            if (!TryGetDoubleVectorData(ref buffer, vector, ref consumed))
                return false;
        }

        value = vector;
        return true;
    }

    private bool TryGetIntVectorData(ref Span<byte> buffer, Vector<int> vector, ref int consumed)
    {
        var value = NetworkBitConverter.ToInt32(buffer);
        vector.Add(value);
        consumed += sizeof(int);
        buffer = buffer[sizeof(int)..];
        return true;

    }

    private bool TryGetUIntVectorData(ref Span<byte> buffer, Vector<uint> vector, ref int consumed)
    {
        var value = NetworkBitConverter.ToUInt32(buffer);
        vector.Add(value);
        consumed += sizeof(uint);
        buffer = buffer[sizeof(uint)..];
        return true;
    }

    private bool TryGetDoubleVectorData(ref Span<byte> buffer, Vector<double> vector, ref int consumed)
    {
        var value = NetworkBitConverter.ToDouble(buffer);
        vector.Add(value);
        consumed += sizeof(double);
        buffer = buffer[sizeof(double)..];
        return true;

    }

    private bool TryGetReference<T, TTableEle>(uint header, List<TTableEle> referenceTable, out uint headerData, out T value, out bool isRef)
    {
        isRef = default;
        value = default;
        headerData = header >> 1;
        if ((header & 0x01) == 0x00)
        {
            var referenceIndex = (int)headerData;
            if (referenceTable.Count <= referenceIndex)
                return false;
            if (referenceTable[referenceIndex] is not T refObject)
                return false;
            value = refObject;
            isRef = true;
            return true;
        }
        isRef = false;
        return true;
    }

    private bool ReadVectorHeader<T>(ref Span<byte> buffer, ref T value, ref int consumed, out int itemCount, out bool isFixedSize, out bool isRef)
    {
        isFixedSize = default;
        itemCount = default;
        isRef = default;
        if (!TryGetU29Impl(buffer, out var header, out var headerLength))
            return false;

        if (!TryGetReference(header, _objectReferenceTable, out var headerData, out T refValue, out isRef))
            return false;

        if (isRef)
        {
            value = refValue;
            consumed = Amf3CommonValues.MarkerLength + headerLength;
            return true;
        }

        itemCount = (int)headerData;

        var objectBuffer = buffer[headerLength..];

        if (objectBuffer.Length < sizeof(byte))
            return false;

        isFixedSize = objectBuffer[0] == 0x01;
        buffer = objectBuffer[sizeof(byte)..];
        consumed = Amf3CommonValues.MarkerLength + headerLength + sizeof(byte);
        return true;
    }

    private bool ReadVectorTypeName(ref Span<byte> typeNameBuffer, out string typeName, out int typeNameConsumed)
    {
        typeName = default;
        typeNameConsumed = default;
        if (!TryGetStringImpl(typeNameBuffer, _stringReferenceTable, out typeName, out typeNameConsumed))
            return false;
        typeNameBuffer = typeNameBuffer[typeNameConsumed..];
        return true;
    }

    public bool TryGetVectorObject(Span<byte> buffer, out object value, out int consumed)
    {
        value = default;
        consumed = default;

        if (!DataIsType(buffer, Amf3Type.VectorObject))
            return false;

        buffer = buffer[Amf3CommonValues.MarkerLength..];

        var arrayConsumed = 0;

        if (!ReadVectorHeader(ref buffer, ref value, ref arrayConsumed, out var itemCount, out var isFixedSize, out var isRef))
            return false;

        if (isRef)
        {
            consumed = arrayConsumed;
            return true;
        }

        if (!ReadVectorTypeName(ref buffer, out var typeName, out var typeNameConsumed))
            return false;

        var arrayBodyBuffer = buffer;

        object resultVector;
        Action<object> addAction;
        if (typeName == "*")
        {
            var v = new Vector<object>();
            _objectReferenceTable.Add(v);
            v.IsFixedSize = isFixedSize;
            resultVector = v;
            addAction = v.Add;
        }
        else
        {
            if (!_registeredTypedObejectStates.TryGetValue(typeName, out var state))
                return false;
            var elementType = state.Type;

            var vectorType = typeof(Vector<>).MakeGenericType(elementType);
            resultVector = Activator.CreateInstance(vectorType);
            _objectReferenceTable.Add(resultVector);
            vectorType.GetProperty("IsFixedSize")?.SetValue(resultVector, isFixedSize);
            var addMethod = vectorType.GetMethod("Add");
            addAction = o => addMethod?.Invoke(resultVector, new[] { o });
        }
        for (var i = 0; i < itemCount; i++)
        {
            if (!TryGetValue(arrayBodyBuffer, out var item, out var itemConsumed))
                return false;
            addAction(item);

            arrayBodyBuffer = arrayBodyBuffer[itemConsumed..];
            arrayConsumed += itemConsumed;
        }
        value = resultVector;
        consumed = typeNameConsumed + arrayConsumed;
        return true;
    }

    public bool TryGetDictionary(Span<byte> buffer, out Amf3Dictionary<object, object> value, out int consumed)
    {
        value = default;
        consumed = default;
        if (!DataIsType(buffer, Amf3Type.Dictionary))
            return false;

        if (!TryGetU29Impl(buffer[Amf3CommonValues.MarkerLength..], out var header, out var headerLength))
            return false;

        if (!TryGetReference(header, _objectReferenceTable, out var headerData, out Amf3Dictionary<object, object> refValue, out var isRef))
            return false;
        if (isRef)
        {
            value = refValue;
            consumed = Amf3CommonValues.MarkerLength + headerLength;
            return true;
        }

        var itemCount = (int)headerData;
        var dictConsumed = 0;
        if (buffer.Length - Amf3CommonValues.MarkerLength - headerLength < sizeof(byte))
            return false;
        var weakKeys = buffer[Amf3CommonValues.MarkerLength + headerLength] == 0x01;

        var dictBuffer = buffer[(Amf3CommonValues.MarkerLength + headerLength + /* weak key flag */ sizeof(byte))..];
        var dict = new Amf3Dictionary<object, object>
        {
            WeakKeys = weakKeys
        };
        _objectReferenceTable.Add(dict);
        for (var i = 0; i < itemCount; i++)
        {
            if (!TryGetValue(dictBuffer, out var key, out var keyConsumed))
                return false;
            dictBuffer = dictBuffer[keyConsumed..];
            dictConsumed += keyConsumed;
            if (!TryGetValue(dictBuffer, out var data, out var dataConsumed))
            {
                return false;
            }
            dictBuffer = dictBuffer[dataConsumed..];
            dict.Add(key, data);
            dictConsumed += dataConsumed;
        }
        value = dict;
        consumed = Amf3CommonValues.MarkerLength + headerLength + dictConsumed + /* weak key flag */ sizeof(byte);
        return true;
    }
}