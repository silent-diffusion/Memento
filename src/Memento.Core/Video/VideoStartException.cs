namespace Memento.Core.Video;

/// <summary>A video source, encoder or file could not start; <see cref="Code"/> is a <c>video.*</c> bridge error code.</summary>
public sealed class VideoStartException : Exception
{
    public VideoStartException()
        : this(Bridge.DomainErrorCodes.VideoUnavailable, "Video can't be recorded here. Audio recording is not affected.")
    {
    }

    public VideoStartException(string message)
        : this(Bridge.DomainErrorCodes.VideoUnavailable, message)
    {
    }

    public VideoStartException(string message, Exception innerException)
        : base(message, innerException)
    {
        Code = Bridge.DomainErrorCodes.VideoUnavailable;
    }

    public VideoStartException(string code, string message)
        : base(message)
    {
        Code = code;
    }

    public VideoStartException(string code, string message, Exception innerException)
        : base(message, innerException)
    {
        Code = code;
    }

    public string Code { get; }
}
