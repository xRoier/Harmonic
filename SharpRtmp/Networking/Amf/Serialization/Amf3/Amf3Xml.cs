using System.Xml;

namespace SharpRtmp.Networking.Amf.Serialization.Amf3;

public class Amf3Xml : XmlDocument
{
    public Amf3Xml()
    {
    }

    public Amf3Xml(XmlNameTable nt) : base(nt) { }

    protected internal Amf3Xml(XmlImplementation imp) : base(imp) { }
}