using System.Diagnostics.CodeAnalysis;

namespace Memento.Core.Library;

/// <summary>
/// Library folder paths (2.1, DESIGN.md §6.1): names joined by <c>/</c>, such as <c>Clients/Acme</c>; <c>null</c> is the
/// root. Folders are data in <c>project.json</c> and <c>folders.json</c>, never real directories, so a name may hold
/// any character but <c>/</c>, <c>\</c> and control characters. Names compare without case.
/// </summary>
public static class FolderPaths
{
    public const char Separator = '/';
    public const int MaxNameLength = 80;
    public const int MaxDepth = 8;

    public static StringComparer Comparer => StringComparer.OrdinalIgnoreCase;

    /// <summary>A folder name, trimmed, or a sentence saying what is wrong with it.</summary>
    public static bool TryName(string? name, [NotNullWhen(true)] out string? trimmed, [NotNullWhen(false)] out string? problem)
    {
        trimmed = name?.Trim();
        problem = null;
        if (string.IsNullOrEmpty(trimmed))
        {
            problem = "A folder needs a name.";
        }
        else if (trimmed.Length > MaxNameLength)
        {
            problem = $"A folder name can be at most {MaxNameLength} characters; this one has {trimmed.Length}.";
        }
        else if (trimmed.Any(c => c is '/' or '\\' || char.IsControl(c)))
        {
            problem = "A folder name can't contain / or \\.";
        }
        else if (trimmed is "." or "..")
        {
            problem = "A folder can't be called . or ..";
        }

        if (problem is not null || trimmed is null)
        {
            trimmed = null;
            problem ??= "A folder needs a name.";
            return false;
        }

        return true;
    }

    /// <summary>A valid path with each name trimmed; <c>false</c> for anything else (empty, too deep, a bad name).</summary>
    public static bool TryNormalize(string? path, [NotNullWhen(true)] out string? normalized)
    {
        normalized = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var names = path.Split(Separator);
        if (names.Length > MaxDepth)
        {
            return false;
        }

        var result = new List<string>(names.Length);
        foreach (var name in names)
        {
            if (!TryName(name, out var trimmed, out _))
            {
                return false;
            }

            result.Add(trimmed);
        }

        normalized = string.Join(Separator, result);
        return true;
    }

    public static string Combine(string? parent, string name) => parent is null ? name : parent + Separator + name;

    /// <summary>The folder holding <paramref name="path"/>; <c>null</c> for a top-level folder.</summary>
    public static string? Parent(string path)
    {
        var at = path.LastIndexOf(Separator);
        return at < 0 ? null : path[..at];
    }

    public static string Name(string path) => path[(path.LastIndexOf(Separator) + 1)..];

    public static int Depth(string path) => path.Count(c => c == Separator) + 1;

    /// <summary><paramref name="path"/> is <paramref name="folder"/> or inside it.</summary>
    public static bool IsWithin(string? path, string folder) =>
        path is not null && (Comparer.Equals(path, folder) || path.StartsWith(folder + Separator, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// <paramref name="path"/> (within <paramref name="from"/>) moved to <paramref name="to"/>: <c>A/B/C</c> with
    /// <c>A/B</c> → <c>X</c> becomes <c>X/C</c>; with <paramref name="to"/> <c>null</c> (the root) it becomes <c>C</c>.
    /// </summary>
    public static string? Rebase(string path, string from, string? to)
    {
        var rest = path.Length == from.Length ? string.Empty : path[(from.Length + 1)..];
        return to is null
            ? rest.Length == 0 ? null : rest
            : rest.Length == 0 ? to : to + Separator + rest;
    }

    /// <summary><paramref name="path"/> and every folder above it, outermost first.</summary>
    public static IEnumerable<string> SelfAndAncestors(string path)
    {
        var at = 0;
        while ((at = path.IndexOf(Separator, at)) >= 0)
        {
            yield return path[..at];
            at++;
        }

        yield return path;
    }
}
