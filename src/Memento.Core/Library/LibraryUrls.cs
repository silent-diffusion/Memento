using Memento.Core.Projects;

namespace Memento.Core.Library;

/// <summary>
/// The second WebView2 virtual host, mapped by the app to the library's <c>projects</c> folder (not the library
/// root, so <c>library.db</c> and anything else beside the project folders is not reachable from the page). The
/// page streams media and reads peaks with plain <c>&lt;audio&gt;</c> and <c>fetch</c>. The UI never builds these
/// URLs; the host returns them.
/// </summary>
public static class LibraryUrls
{
    public const string VirtualHost = "library.memento";
    public const string Origin = "https://" + VirtualHost + "/";

    /// <summary>
    /// The folder served at <see cref="Origin"/>: <c>&lt;library&gt;/projects</c>. The browser removes <c>.</c> and
    /// <c>..</c> segments before a URL path is resolved below this folder, so a URL can never name a file outside it.
    /// </summary>
    public static string MappedFolder(string libraryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(libraryRoot);
        return Path.Combine(Path.GetFullPath(libraryRoot), ProjectLayout.ProjectsFolder);
    }

    /// <summary>
    /// <c>https://library.memento/&lt;id&gt;/&lt;relative file&gt;</c>, each segment escaped. Refuses <c>.</c> and
    /// <c>..</c> segments (and an id with a separator), so a URL only ever names a file inside that project's folder.
    /// </summary>
    public static string ForProjectFile(string recordingId, string relativeFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFile);
        var id = Segment(recordingId, nameof(recordingId));
        var segments = relativeFile
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => Segment(s, nameof(relativeFile)));
        return $"{Origin}{id}/{string.Join('/', segments)}";
    }

    private static string Segment(string value, string parameter)
    {
        if (value.Trim('.').Length == 0 || value.Contains('/', StringComparison.Ordinal) || value.Contains('\\', StringComparison.Ordinal))
        {
            throw new ArgumentException($"'{value}' is not a file or folder name inside a project.", parameter);
        }

        return Uri.EscapeDataString(value);
    }
}
