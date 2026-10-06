namespace Memento.Core.Library;

/// <summary>Values of <c>library.list</c>'s <c>sort</c>.</summary>
public static class LibrarySort
{
    public const string Newest = "newest";
    public const string Oldest = "oldest";
    public const string Longest = "longest";
    public const string Title = "title";

    public static IReadOnlyList<string> All { get; } = [Newest, Oldest, Longest, Title];

    public static bool IsValid(string? sort) => sort is not null && All.Contains(sort, StringComparer.Ordinal);
}
