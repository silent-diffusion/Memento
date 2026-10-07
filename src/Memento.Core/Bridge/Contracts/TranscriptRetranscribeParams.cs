namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.retranscribe</c>. Omitted fields use Settings › Transcription.</summary>
public sealed record TranscriptRetranscribeParams
{
    public required string RecordingId { get; init; }

    public string? ModelId { get; init; }

    public string? Language { get; init; }
}
