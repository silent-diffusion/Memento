namespace Memento.Core.Projects;

/// <summary>
/// Walking the library without following junctions or symbolic links. <see cref="SearchOption.AllDirectories"/> recurses
/// into a junction, so a link planted inside a project folder made project delete clear the read-only flag on files
/// elsewhere, library move copy whatever it pointed at into the new library, and sizes count it (or loop on a cycle).
/// </summary>
public static class LinkSafeFiles
{
    /// <summary>Every file below a folder, hidden ones included, never through a junction or symbolic link.</summary>
    public static EnumerationOptions Recursive { get; } = new()
    {
        RecurseSubdirectories = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
        IgnoreInaccessible = false,
        ReturnSpecialDirectories = false,
    };

    /// <summary>
    /// Deletes <paramref name="folder"/> and everything in it (or only its contents with <paramref name="keepFolder"/>):
    /// junctions and symbolic links are removed as links first (their targets are never entered), read-only files are made
    /// writable, then the rest goes. .NET's recursive delete refuses a folder that holds a junction ("access denied").
    /// A file another program holds briefly is retried (<see cref="DeleteBudget"/>).
    /// </summary>
    public static void DeleteTree(string folder, bool keepFolder = false) => DeleteTree(folder, keepFolder, wait: null);

    /// <param name="wait">Sleeps between tries (tests replace it).</param>
    internal static void DeleteTree(string folder, bool keepFolder, Action<int>? wait)
    {
        if (!Directory.Exists(folder))
        {
            return;
        }

        // Find the links without ever descending into one.
        var links = new List<FileSystemInfo>();
        var pending = new Stack<DirectoryInfo>();
        pending.Push(new DirectoryInfo(folder));
        var everything = new EnumerationOptions { AttributesToSkip = 0, IgnoreInaccessible = false };
        while (pending.TryPop(out var directory))
        {
            foreach (var entry in directory.EnumerateFileSystemInfos("*", everything))
            {
                if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    links.Add(entry);
                }
                else if (entry is DirectoryInfo child)
                {
                    pending.Push(child);
                }
            }
        }

        foreach (var link in links)
        {
            if (link is DirectoryInfo directory)
            {
                directory.Delete(recursive: false);
            }
            else
            {
                link.Delete();
            }
        }

        foreach (var file in Directory.EnumerateFiles(folder, "*", Recursive))
        {
            var attributes = File.GetAttributes(file);
            if ((attributes & FileAttributes.ReadOnly) != 0)
            {
                File.SetAttributes(file, attributes & ~FileAttributes.ReadOnly);
            }
        }

        if (!keepFolder)
        {
            Retrying(() => Directory.Delete(folder, recursive: true), () => !Directory.Exists(folder), wait);
            return;
        }

        foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
        {
            if (Directory.Exists(entry))
            {
                Retrying(() => Directory.Delete(entry, recursive: true), () => !Directory.Exists(entry), wait);
            }
            else
            {
                Retrying(() => File.Delete(entry), () => !File.Exists(entry), wait);
            }
        }
    }

    /// <summary>
    /// How long a delete keeps retrying a file another program holds, counted in the waits between tries (not on the
    /// clock, so a starved machine still gets every try): a virus scanner or indexer reading a file Memento just wrote,
    /// or a reader that shares delete (the file then stays "delete pending" and the folder is not empty until it
    /// closes). Under load such a read outlasts a single try; a program that really keeps the file fails as before.
    /// </summary>
    internal static readonly TimeSpan DeleteBudget = TimeSpan.FromSeconds(10);

    private static void Retrying(Action delete, Func<bool> gone, Action<int>? wait)
    {
        wait ??= Thread.Sleep;
        var waited = 0;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                delete();
                return;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException && ex is not PathTooLongException)
            {
                if (gone())
                {
                    // Removed in between (the last reader closed a delete-pending file): nothing left to do.
                    return;
                }

                if (waited >= DeleteBudget.TotalMilliseconds || Audio.DiskErrors.IsDiskFull(ex))
                {
                    throw;
                }

                var ms = Math.Min(100, 5 << Math.Min(attempt - 1, 5));
                waited += ms;
                wait(ms);
            }
        }
    }

    /// <summary>
    /// <paramref name="path"/> with every existing junction or symbolic link along it replaced by its final target (the
    /// part that does not exist yet is kept as written), so two paths can be compared by what they really are.
    /// </summary>
    public static string RealPath(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full) ?? string.Empty;
        var segments = full[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
        var current = root;
        var i = 0;
        for (; i < segments.Length; i++)
        {
            var next = Path.Combine(current, segments[i]);
            try
            {
                var info = new DirectoryInfo(next);
                if (!info.Exists)
                {
                    break;
                }

                current = info.LinkTarget is not null && info.ResolveLinkTarget(returnFinalTarget: true) is { } target
                    ? Path.GetFullPath(target.FullName)
                    : next;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                break;
            }
        }

        for (; i < segments.Length; i++)
        {
            current = Path.Combine(current, segments[i]);
        }

        return current;
    }
}
