using Memento.Core.Projects;

namespace Memento.Core.Library;

/// <summary>
/// Which requests to <c>https://library.memento/</c> the app answers. The page only ever loads a project's mix and its
/// peaks (<see cref="LibraryUrls.ForProjectFile"/>), so everything else is refused before WebView2 reads a file: another
/// file type (transcripts, documents, keys never live there, but a request for them is still refused), a name with an
/// alternate data stream (<c>:</c>), a folder that is not a project id, and any file reached through a junction or
/// symbolic link inside the projects folder, which could otherwise point anywhere on the disk.
/// </summary>
public static class LibraryResourcePolicy
{
    /// <summary>The mix formats the app writes (finalize, optimize) and the peaks file.</summary>
    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase) { ".flac", ".wav", ".m4a", ".mp3" };

    private const string PeaksFile = "peaks.json";

    /// <summary>
    /// The file a library URL may serve, or null when the request must be refused. <paramref name="mappedFolder"/> is the
    /// folder mapped to the virtual host (<see cref="LibraryUrls.MappedFolder"/>).
    /// </summary>
    public static string? ServableFile(string mappedFolder, Uri uri)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mappedFolder);
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !string.Equals(uri.Host, LibraryUrls.VirtualHost, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort)
        {
            return null;
        }

        string[] segments;
        try
        {
            segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.UnescapeDataString).ToArray();
        }
        catch (UriFormatException)
        {
            return null;
        }

        // <project id>/<file>: the mix and peaks sit at the top of the project folder.
        if (segments.Length != 2 || !ProjectId.IsValid(segments[0]) || !IsPlainName(segments[1]))
        {
            return null;
        }

        var name = segments[1];
        if (!string.Equals(name, PeaksFile, StringComparison.OrdinalIgnoreCase) && !MediaExtensions.Contains(Path.GetExtension(name)))
        {
            return null;
        }

        var root = Path.GetFullPath(mappedFolder);
        var project = Path.Combine(root, segments[0]);
        var file = Path.GetFullPath(Path.Combine(project, name));
        if (!file.StartsWith(project + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return IsReparsePoint(project) || IsReparsePoint(file) ? null : file;
    }

    private static bool IsPlainName(string name) =>
        name.Trim('.').Length > 0
        && name.Trim().Length == name.Length
        && !name.EndsWith('.')
        && name.IndexOfAny(['/', '\\', ':', '\0']) < 0
        && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

    /// <summary>True for a junction or symbolic link, and for anything that cannot be read (a missing file is refused too).</summary>
    private static bool IsReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return true;
        }
    }
}
