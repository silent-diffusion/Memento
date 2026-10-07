namespace Memento.Core.Models;

/// <summary><c>catalog.json</c>: every model the model manager can install.</summary>
public sealed record ModelCatalogDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; }

    public IReadOnlyList<ModelCatalogEntry> Models { get; init; } = [];
}
