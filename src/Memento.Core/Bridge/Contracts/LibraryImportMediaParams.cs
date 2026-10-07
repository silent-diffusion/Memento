namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>library.importMedia</c>. Without <see cref="Path"/> the host shows the file picker.</summary>
public sealed record LibraryImportMediaParams
{
    public string? Path { get; init; }

    /// <summary>Defaults to the file name without its extension.</summary>
    public string? Title { get; init; }

    /// <summary>Defaults to Settings › Recording › Recording type.</summary>
    public string? Type { get; init; }
}
