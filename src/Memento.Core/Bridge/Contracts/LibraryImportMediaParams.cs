namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Parameters of <c>library.importMedia</c>. The host shows its file picker; <see cref="Path"/> is refused with
/// <c>bridge.invalidParams</c> (<see cref="PickedFilesOnly"/>) and is kept only so the request shape stays stable.
/// </summary>
public sealed record LibraryImportMediaParams
{
    public string? Path { get; init; }

    /// <summary>Defaults to the file name without its extension.</summary>
    public string? Title { get; init; }

    /// <summary>Defaults to Settings › Recording › Recording type.</summary>
    public string? Type { get; init; }
}
