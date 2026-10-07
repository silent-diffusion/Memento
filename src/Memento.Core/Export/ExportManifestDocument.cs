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
}
