namespace Memento.AI.Local;

/// <summary>
/// A local LLM in <c>local-models.json</c>: the fields of Core's <c>ModelCatalogEntry</c> (so the integration can
/// merge the file into Core's catalog; <c>llm</c> lands in its extension data) plus <see cref="Llm"/>.
/// </summary>
public sealed record LocalModelEntry
{
    public required string Id { get; init; }

    public required string Engine { get; init; }

    public required string Kind { get; init; }

    public string? Role { get; init; }

    public required string Name { get; init; }

    public required string Description { get; init; }

    public required string FileName { get; init; }

    public long SizeBytes { get; init; }

    public required string Sha256 { get; init; }

    public required string Url { get; init; }

    public required string License { get; init; }

    public string Notes { get; init; } = string.Empty;

    public required string RunsOn { get; init; }

    public long? MinVramBytes { get; init; }

    public string? RecommendedFor { get; init; }

    public required string AccuracyNote { get; init; }

    public required LocalModelProfile Llm { get; init; }
}
