namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.copy</c>: the readable transcript on the clipboard.</summary>
public sealed record TranscriptCopyParams
{
    public required string RecordingId { get; init; }

    /// <summary><c>text</c> or <c>markdown</c>.</summary>
    public required string Format { get; init; }

    /// <summary>Timestamps, speakers and layout; missing or <c>null</c> means the defaults.</summary>
    public TranscriptTextOptions? Options { get; init; }

    /// <summary>Only these lines (Review's filtered view), in transcript order; missing or <c>null</c> copies every line.</summary>
    public IReadOnlyList<string>? SegmentIds { get; init; }
}
