namespace Memento.Core.Host;

/// <summary>One entry of a file picker's type list, e.g. "Word documents" with <c>*.docx</c>.</summary>
public sealed record FileFilter(string Name, IReadOnlyList<string> Patterns)
{
    /// <summary>The WPF/Win32 filter string: <c>Name|*.a;*.b</c>.</summary>
    public static string ToFilterString(IReadOnlyList<FileFilter> filters) =>
        string.Join('|', filters.Select(f => $"{f.Name}|{string.Join(';', f.Patterns)}"));
}
