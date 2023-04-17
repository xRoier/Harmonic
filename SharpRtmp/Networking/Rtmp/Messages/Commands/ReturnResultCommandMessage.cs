using SharpRtmp.Networking.Rtmp.Serialization;

namespace SharpRtmp.Networking.Rtmp.Messages.Commands;

public class ReturnResultCommandMessage : CallCommandMessage
{
    [OptionalArgument]
    public object ReturnValue { get; set; }
    private bool _success = true;
    public bool IsSuccess
    {
        get => _success;
        set
        {
            ProcedureName = value ? "_result" : "_error";
            _success = value;
        }
    }

    public ReturnResultCommandMessage(AmfEncodingVersion encoding) : base(encoding)
    {
        IsSuccess = true;
    }
}