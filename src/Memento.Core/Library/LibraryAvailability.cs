using Memento.Core.Bridge;

namespace Memento.Core.Library;

/// <summary>
/// Whether the library folder can be used. The default library (<c>%LOCALAPPDATA%\Memento\Library</c>) is simply
/// created on first run. A library somewhere else that is missing (a USB drive that is not plugged in, a folder that
/// was renamed) is reported instead and never re-created empty, so recordings never go to the wrong place and the
/// person is told what happened. <see cref="Unavailable"/> is set at launch and cleared once the folder is back.
/// </summary>
public sealed class LibraryAvailability(ILibraryLocation library)
{
    private string? _unavailable;

    /// <summary>Why the library cannot be used right now, in words, or <c>null</c>.</summary>
    public string? Unavailable => Volatile.Read(ref _unavailable);

    public static bool IsDefault(string root) =>
        string.Equals(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar), Path.GetFullPath(AppPaths.DefaultLibrary).TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);

    /// <summary>"The library folder R:\Memento Library is not available because drive R: is not connected."</summary>
    public static string Describe(string root)
    {
        var drive = Path.GetPathRoot(Path.GetFullPath(root));
        return !string.IsNullOrEmpty(drive) && !Directory.Exists(drive)
            ? $"The library folder {root} is not available because drive {drive.TrimEnd(Path.DirectorySeparatorChar)} is not connected."
            : $"The library folder {root} is not there: it may have been moved, renamed or deleted.";
    }

    /// <summary>
    /// At launch: creates the default library if needed and returns <c>true</c>; for a missing library elsewhere,
    /// remembers why and returns <c>false</c>.
    /// </summary>
    public bool Check()
    {
        var root = library.Root;
        if (Directory.Exists(root))
        {
            Volatile.Write(ref _unavailable, null);
            return true;
        }

        if (IsDefault(root))
        {
            Directory.CreateDirectory(root);
            Volatile.Write(ref _unavailable, null);
            return true;
        }

        Volatile.Write(ref _unavailable, Describe(root));
        return false;
    }

    /// <summary>
    /// Before using the library: throws <c>library.unavailable</c> while it is still missing; returns <c>true</c> once
    /// when it came back (the caller opens it), else <c>false</c>.
    /// </summary>
    /// <param name="refusal">What did not happen, e.g. "The recording did not start and nothing was recorded."</param>
    public bool EnsureAvailable(string refusal)
    {
        if (Unavailable is null)
        {
            return false;
        }

        var root = library.Root;
        if (!Directory.Exists(root) && !IsDefault(root))
        {
            throw new BridgeException(
                DomainErrorCodes.LibraryUnavailable,
                $"{Describe(root)} {refusal} The recordings in it are not affected. Reconnect the drive or put the folder back, or choose another location in Settings › General.",
                root);
        }

        return Interlocked.Exchange(ref _unavailable, null) is not null;
    }
}
