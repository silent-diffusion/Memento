namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.search</c>.</summary>
public sealed record TranscriptSearchParams
{
    public required string RecordingId { get; init; }

    public required string Query { get; init; }
}
