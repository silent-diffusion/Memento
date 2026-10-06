namespace Memento.Audio.Codecs;

/// <summary>An encode failed. The message names the file, the amount, what is safe, and the fix.</summary>
public sealed class AudioEncodeException : Exception
{
    public AudioEncodeException()
    {
    }

    public AudioEncodeException(string message)
        : base(message)
    {
    }

    public AudioEncodeException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AudioEncodeException(AudioEncodeErrorCode code, string message, Exception? innerException = null)
        : base(message, innerException)
    {
        Code = code;
    }

    public AudioEncodeErrorCode Code { get; } = AudioEncodeErrorCode.EncodeFailed;

    /// <summary>Bytes the operation needed (temp space failures).</summary>
    public long? RequiredBytes { get; init; }

    /// <summary>Bytes that were available (temp space failures).</summary>
    public long? AvailableBytes { get; init; }
}
