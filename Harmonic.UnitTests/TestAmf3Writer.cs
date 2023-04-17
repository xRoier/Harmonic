﻿using System;
using System.Linq;
using System.Xml;
using Harmonic.Networking.Amf.Common;
using Harmonic.Networking.Amf.Serialization.Amf3;
using Harmonic.Networking.Amf.Serialization.Attributes;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Harmonic.Unit;

[TestClass]
public class TestAmf3Writer
{
    [TestMethod]
    public void TestDouble()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();
        var random = new Random();

        using var sc = new SerializationContext();
        for (var i = 0; i < 1000; i++)
        {
            var value = random.NextDouble();

            writer.WriteBytes(value, sc);
            var buffer = new byte[sc.MessageLength];
            sc.GetMessage(buffer);
            reader.TryGetDouble(buffer, out var readValue, out var consumed);
            Assert.AreEqual(readValue, value);
            Assert.AreEqual(consumed, buffer.Length);
        }
    }

    [TestMethod]
    public void TestInteger()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();
        var backend = new byte[5];

        using var sc = new SerializationContext();
        for (var i = 0; i <= Amf3Writer.U29Max; i += 0xFF)
        {
            var value = (uint)i;
            writer.WriteBytes(value, sc);
            var buffer = backend.AsSpan(0, sc.MessageLength);
            buffer.Clear();
            sc.GetMessage(buffer);
            Assert.IsTrue(reader.TryGetUInt29(buffer, out var readValue, out var consumed));
            Assert.AreEqual(readValue, value);
            Assert.AreEqual(consumed, buffer.Length);
        }
    }

    [TestMethod]
    public void TestBoolean()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();

        using var sc = new SerializationContext();
        writer.WriteBytes(true, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);
        Assert.IsTrue(reader.TryGetBoolean(buffer, out var readVal, out var consumed));
        Assert.AreEqual(buffer.Length, consumed);
        Assert.IsTrue(readVal);

        writer.WriteBytes(false, sc);
        sc.GetMessage(buffer);
        Assert.IsTrue(reader.TryGetBoolean(buffer, out readVal, out consumed));
        Assert.AreEqual(buffer.Length, consumed);
        Assert.IsFalse(readVal);
    }

    [TestMethod]
    public void TestNull()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();

        using var sc = new SerializationContext();
        writer.WriteBytes((object)null, sc);
        var buffer = new byte[sc.MessageLength];

        sc.GetMessage(buffer);

        Assert.IsTrue(reader.TryGetNull(buffer, out var readVal, out var consumed));
        Assert.IsNull(readVal);
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestUndefined()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();

        using var sc = new SerializationContext();
        writer.WriteBytes(new Undefined(), sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        Assert.IsTrue(reader.TryGetUndefined(buffer, out var readVal, out var consumed));
        Assert.IsNotNull(readVal);
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestArray()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();

        var arr = new Amf3Array
        {
            ["a"] = (uint)1,
            ["b"] = 2.1,
            ["d"] = null
        };

        arr.DensePart.Add(1);
        arr.DensePart.Add(1.2);

        using var sc = new SerializationContext();
        writer.WriteBytes(arr, sc);

        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        Assert.IsTrue(reader.TryGetArray(buffer, out var readVal, out var consumed));
        Assert.AreEqual(arr["a"], readVal["a"]);
        Assert.AreEqual(arr["b"], readVal["b"]);
        Assert.AreEqual(arr["d"], readVal["d"]);
        Assert.AreEqual(1.0, readVal[0]);
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestByteArray()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();

        using var sc = new SerializationContext();
        var arr = new byte[] { 1, 2, 3 };
        writer.WriteBytes(arr, sc);

        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        Assert.IsTrue(reader.TryGetByteArray(buffer, out var readVal, out var consumed));
        Assert.IsTrue(arr.SequenceEqual(readVal));
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestDate()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();

        using var sc = new SerializationContext();
        var date = DateTime.Now;
        writer.WriteBytes(date, sc);

        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        Assert.IsTrue(reader.TryGetDate(buffer, out var readVal, out _));
        Assert.AreEqual(date.Year, readVal.Year);
        Assert.AreEqual(date.Month, readVal.Month);
        Assert.AreEqual(date.Day, readVal.Day);
        Assert.AreEqual(date.Hour, readVal.Hour);
        Assert.AreEqual(date.Minute, readVal.Minute);
        Assert.AreEqual(date.Second, readVal.Second);
        Assert.AreEqual(date.Millisecond, readVal.Millisecond);
    }

    [TestMethod]
    public void TestDictionary()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();

        var dict = new Amf3Dictionary<object, object>
        {
            { "ss", 1.0 },
            { "sd", new Vector<int> { 1, 2 } },
            { new Vector<int> { 1, 2 }, "sd" }
        };

        using var sc = new SerializationContext();
        writer.WriteBytes(dict, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        Assert.IsTrue(reader.TryGetDictionary(buffer, out var readVal, out var consumed));
        Assert.AreEqual(dict["ss"], readVal["ss"]);
        Assert.AreEqual(dict["sd"], readVal["sd"]);
        Assert.AreEqual(dict[new Vector<int> { 1, 2 }], readVal[new Vector<int> { 1, 2 }]);
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestIExternalizable()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();

        reader.RegisterExternalizable<Iexternalizable>();

        var ext = new Iexternalizable
        {
            V1 = 0.1,
            V2 = 1
        };

        using var sc = new SerializationContext();
        writer.WriteBytes(ext, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        Assert.IsTrue(reader.TryGetObject(buffer, out var readVal, out var consumed));
        var val = (Iexternalizable)readVal;

        Assert.AreEqual(val.V1, ext.V1);
        Assert.AreEqual(val.V2, ext.V2);
        Assert.AreEqual(buffer.Length, consumed);
    }

    public class TestCls2: IEquatable<TestCls2>
    {
        [ClassField]
        public double T1 {get;set;}

        public bool Equals(TestCls2 other) => T1 == other.T1;

        public override bool Equals(object obj)
        {
            if (obj is TestCls2 obj2)
                return Equals(obj2);
            return base.Equals(obj);
        }

        public override int GetHashCode() => HashCode.Combine(T1);
    }

    [TestMethod]
    public void TestObject()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();

        var obj = new AmfObject
        {
            { "t1", (uint)2 },
            { "t2", 3.1 }
        };
        obj.AddDynamic("t3", new Vector<int> { 2, 3, 4 });

        using var sc = new SerializationContext();
        writer.WriteBytes(obj, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        Assert.IsTrue(reader.TryGetObject(buffer, out var readVal, out var consumed));
        var readObj = (AmfObject)readVal;
        Assert.AreEqual(readObj.Fields["t1"], (uint)2);
        Assert.AreEqual(readObj.Fields["t2"], 3.1);
        Assert.AreEqual(readObj.DynamicFields["t3"], new Vector<int> { 2, 3, 4 });
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestObject2()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();
        reader.RegisterTypedObject<TestCls2>();

        var obj = new TestCls2
        {
            T1 = 3.5
        };

        using var sc = new SerializationContext();
        writer.WriteBytes(obj, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        Assert.IsTrue(reader.TryGetObject(buffer, out var readVal, out var consumed));
        Assert.AreEqual(obj, readVal);
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestObject3()
    {
        var reader = new Amf3Reader();
        var writer = new Amf3Writer();
        reader.RegisterTypedObject<TestCls>();
            
        var t = new TestCls
        {
            T1 = 3.3,
            T2 = "abc",
            T3 = "abd",
            t4 = new Vector<int> { 2000, 30000, 400000 }
        };

        using var sc = new SerializationContext();
        writer.WriteBytes(t, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        Assert.IsTrue(reader.TryGetObject(buffer, out var readVal, out var consumed));
        Assert.AreEqual(t, readVal);
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestXmlDocument()
    {
        var writer = new Amf3Writer();
        var reader = new Amf3Reader();

        using var sc = new SerializationContext();
        var xml = new XmlDocument();
        var elem = xml.CreateElement("price");
        xml.AppendChild(elem);
        writer.WriteBytes(xml, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        Assert.IsTrue(reader.TryGetXmlDocument(buffer, out var ud, out var consunmed));
        Assert.IsNotNull(ud);
        Assert.AreNotEqual(ud.GetElementsByTagName("price").Count, 0);
        Assert.AreEqual(consunmed, buffer.Length);
    }

    [TestMethod]
    public void TestXml()
    {
        var writer = new Amf3Writer();
        var reader = new Amf3Reader();

        using var sc = new SerializationContext();
        var xml = new Amf3Xml();
        var elem = xml.CreateElement("price");
        xml.AppendChild(elem);
        writer.WriteBytes(xml, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        Assert.IsTrue(reader.TryGetXml(buffer, out var ud, out var consunmed));
        Assert.IsNotNull(ud);
        Assert.AreNotEqual(ud.GetElementsByTagName("price").Count, 0);
        Assert.AreEqual(consunmed, buffer.Length);
    }

    [TestMethod]
    public void TestVectorUint()
    {
        var writer = new Amf3Writer();
        var reader = new Amf3Reader();

        using var sc = new SerializationContext();
        var v = new Vector<uint> { 2, 3, 4 };
        writer.WriteBytes(v, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        reader.TryGetVectorUint(buffer, out var readVal, out var consumed);
        Assert.AreEqual(v, readVal);
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestVectorInt()
    {
        var writer = new Amf3Writer();
        var reader = new Amf3Reader();

        using var sc = new SerializationContext();
        var v = new Vector<int> { 2, 3, 4 };
        writer.WriteBytes(v, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        reader.TryGetVectorInt(buffer, out var readVal, out var consumed);
        Assert.AreEqual(v, readVal);
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestVectorDouble()
    {
        var writer = new Amf3Writer();
        var reader = new Amf3Reader();

        using var sc = new SerializationContext();
        var v = new Vector<double> { 2, 3, 4 };
        writer.WriteBytes(v, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        reader.TryGetVectorDouble(buffer, out var readVal, out var consumed);
        Assert.AreEqual(v, readVal);
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestVectorTypedObject()
    {
        var writer = new Amf3Writer();
        var reader = new Amf3Reader();

        reader.RegisterTypedObject<TestCls>();

        var t = new TestCls
        {
            T1 = 3.3,
            T2 = "abc",
            T3 = "abd",
            t4 = new Vector<int> { 2000, 30000, 400000 }
        };
        t.AddDynamic("t5", new Vector<TestCls> { new() { T1 = 5.6 } });

        using var sc = new SerializationContext();
        var v = new Vector<TestCls> { t, t, t };
        writer.WriteBytes(v, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        reader.TryGetVectorObject(buffer, out var readVal, out var consumed);
        Assert.IsTrue(readVal.GetType().GetGenericArguments().First() == typeof(TestCls));
        Assert.AreEqual(v, readVal);
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestVectorAnyObject()
    {
        var writer = new Amf3Writer();
        var reader = new Amf3Reader();

        reader.RegisterTypedObject<TestCls>();

        var t = new TestCls
        {
            T1 = 3.3,
            T2 = "abc",
            T3 = "abd",
            t4 = new Vector<int> { 2000, 30000, 400000 }
        };

        using var sc = new SerializationContext();
        var v = new Vector<object> { t, 3.2, 4.5 };
        writer.WriteBytes(v, sc);
        var buffer = new byte[sc.MessageLength];
        sc.GetMessage(buffer);

        reader.TryGetVectorObject(buffer, out var readVal, out var consumed);

        Assert.IsTrue(readVal.GetType().GetGenericArguments().First() == typeof(object));
        Assert.AreEqual(v, readVal);
        Assert.AreEqual(buffer.Length, consumed);
    }

    [TestMethod]
    public void TestString()
    {
        var writer = new Amf3Writer();
        var reader = new Amf3Reader();

        using var sc = new SerializationContext();
        for (var i = 0; i < 1000; i++)
        {
            var str = Guid.NewGuid().ToString();
            writer.WriteBytes(str, sc);
            var buffer = new byte[sc.MessageLength];
            sc.GetMessage(buffer);

            Assert.IsTrue(reader.TryGetString(buffer, out var readVal, out var consumed));
            Assert.AreEqual(str, readVal);
            Assert.AreEqual(buffer.Length, consumed);
        }
    }
}