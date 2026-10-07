namespace Memento.Documents.Render;

/// <summary>A paper read back from markup: the title and the rows of modules, in order.</summary>
public sealed record ParsedPaper(string? Title, IReadOnlyList<IReadOnlyList<ParsedModule>> Rows);
