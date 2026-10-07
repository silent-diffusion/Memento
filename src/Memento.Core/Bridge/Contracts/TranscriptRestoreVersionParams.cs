namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>transcript.restoreVersion</c>.</summary>
public sealed record TranscriptRestoreVersionParams
{
    public required string RecordingId { get; init; }

    public required string VersionId { get; init; }
}
