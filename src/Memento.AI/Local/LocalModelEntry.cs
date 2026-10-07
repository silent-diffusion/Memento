namespace Memento.AI.Local;

/// <summary>
/// A local LLM: the fields of its <c>kind: "llm"</c> entry in Core's <c>catalog.json</c> plus <see cref="Llm"/>, read from
/// the entry's <c>llm</c> block (<see cref="LocalModelCatalog.ToLocal"/>).
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
