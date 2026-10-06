namespace Memento.Audio.Capture;

/// <summary>A source could not be opened. The message names it and says what to do.</summary>
public sealed class AudioSourceUnavailableException : Exception
{
    public AudioSourceUnavailableException()
    {
    }

    public AudioSourceUnavailableException(string message)
        : base(message)
    {
    }

    public AudioSourceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public AudioSourceUnavailableException(AudioSourceId source, string message, int hResult = 0, Exception? innerException = null)
        : base(message, innerException)
    {
        SourceId = source;
        HResult = hResult;
    }

    /// <summary>The source that could not be opened.</summary>
    public AudioSourceId SourceId { get; }
}
