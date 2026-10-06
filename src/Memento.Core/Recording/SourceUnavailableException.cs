namespace Memento.Core.Recording;

/// <summary>A source could not be opened (unplugged, blocked by Windows privacy settings, app exited).</summary>
public sealed class SourceUnavailableException : Exception
{
    public SourceUnavailableException(string sourceId, string sourceName, string reason)
        : base($"{sourceName} could not be opened: {reason}")
    {
        SourceId = sourceId;
        SourceName = sourceName;
        Reason = reason;
    }

    public SourceUnavailableException()
        : this(string.Empty, "A source", "it is not available")
    {
    }

    public SourceUnavailableException(string message)
        : this(string.Empty, "A source", message)
    {
    }

    public SourceUnavailableException(string message, Exception innerException)
        : base(message, innerException)
    {
        SourceId = string.Empty;
        SourceName = "A source";
        Reason = message;
    }

    public string SourceId { get; }

    public string SourceName { get; }

    /// <summary>Plain words, e.g. "Windows is blocking microphone access".</summary>
    public string Reason { get; }
}
