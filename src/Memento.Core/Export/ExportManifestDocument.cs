using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Export;

/// <summary>
/// <c>manifest.json</c> beside exported files (ARCHITECTURE.md §4): every file with its size and SHA-256, so a copy
/// can be checked later without Memento.
/// </summary>
public sealed record ExportManifestDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public string App { get; init; } = "Memento";

    public required string MementoVersion { get; init; }

    public required string RecordingId { get; init; }

    public required string Title { get; init; }

    public DateTimeOffset ExportedAt { get; init; }

    public string Algorithm { get; init; } = "sha256";

    public IReadOnlyList<ExportManifestFile> Files { get; init; } = [];

    /// <summary>
    /// How the Markdown and text transcript files were written (timestamps, speakers, layout; after 1.2.0), or <c>null</c>
    /// when the export has neither. An additive field: schema 1 readers ignore it.
    /// </summary>
    public TranscriptTextOptions? TranscriptOptions { get; init; }
}
