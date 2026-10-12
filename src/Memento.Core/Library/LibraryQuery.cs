namespace Memento.Core.Library;

/// <summary>A Library filter: free-text search, a type (or <c>null</c> for all) and one of <see cref="LibrarySort"/>.</summary>
public sealed record LibraryQuery(string? Text, string? Type, string Sort)
{
    public static LibraryQuery All { get; } = new(null, null, LibrarySort.Newest);

    /// <summary>2.1: only the recordings filed directly in <see cref="Folder"/> (<c>null</c>: the root); otherwise every folder.</summary>
    public bool ByFolder { get; init; }

    public string? Folder { get; init; }
}
