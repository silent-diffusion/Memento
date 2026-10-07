namespace Memento.Core.Transcripts;

/// <summary>A kept version: <c>versions/transcript.&lt;utc-stamp&gt;.json</c>.</summary>
/// <param name="Reason">How the kept content came about (its <see cref="TranscriptDocument.LastChange"/>).</param>
public sealed record TranscriptVersionFile(int SchemaVersion, string Id, DateTimeOffset SavedAt, string Reason, TranscriptDocument Transcript)
{
    public const int CurrentSchemaVersion = 1;
}
