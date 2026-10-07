namespace Memento.AI.Local;

/// <summary><c>local-models.json</c>: the downloadable local LLMs.</summary>
public sealed record LocalModelCatalogDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; }

    public IReadOnlyList<LocalModelEntry> Models { get; init; } = [];
}
