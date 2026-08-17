namespace FullBleed.DotNet;

public sealed class FullBleedException : Exception
{
    public FullBleedException(FullBleedStatusCode statusCode, string message)
        : base(message)
    {
        StatusCode = statusCode;
    }

    public FullBleedStatusCode StatusCode { get; }
}
