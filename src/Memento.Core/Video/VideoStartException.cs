namespace Memento.Core.Video;

/// <summary>A video source, encoder or file could not start; <see cref="Code"/> is one of <see cref="VideoProblemCodes"/> that is also an error code.</summary>
public sealed class VideoStartException : Exception
{
    public VideoStartException()
        : this(Bridge.DomainErrorCodes.VideoSourceNotFound, "Video can't be recorded here. Audio recording is not affected.")
    {
    }

    public VideoStartException(string message)
        : this(Bridge.DomainErrorCodes.VideoSourceNotFound, message)
    {
    }

    public VideoStartException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = Bridge.DomainErrorCodes.VideoSourceNotFound;
    }

    public VideoStartException(string code, string message, string? detail = null)
        : base(message)
    {
        Code = code;
        Detail = detail;
    }

    public VideoStartException(string code, string message, Exception innerException, string? detail = null)
        : base(message, innerException)
    {
        Code = code;
        Detail = detail;
    }

    public string Code { get; }

    /// <summary>The other app holding a camera, or the privacy switch that is off.</summary>
    public string? Detail { get; }
}
