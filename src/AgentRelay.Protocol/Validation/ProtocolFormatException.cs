namespace AgentRelay.Protocol;

/// <summary>A parse/format failure that the verifier classifies as an argument error (exit 2).</summary>
public sealed class ProtocolFormatException : Exception
{
    public ProtocolFormatException(string code, string message, string? field = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
        Field = field;
    }

    public string Code { get; }

    public string? Field { get; }
}
