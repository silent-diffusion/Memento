namespace Memento.Core.Bridge;

/// <summary>
/// Thrown by a bridge method to answer with a specific, user-readable error instead of a result.
/// The router copies <see cref="Code"/>, <see cref="Exception.Message"/> and <see cref="Detail"/> into the response.
/// </summary>
public sealed class BridgeException : Exception
{
    public BridgeException(string code, string message, string? detail = null)
        : base(message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        Code = code;
        Detail = detail;
    }

    public BridgeException()
        : this(BridgeErrorCodes.Internal, "The host could not complete the request.")
    {
    }

    public BridgeException(string message)
        : this(BridgeErrorCodes.Internal, message)
    {
    }

    public BridgeException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = BridgeErrorCodes.Internal;
    }

    public string Code { get; }

    public string? Detail { get; }
}
