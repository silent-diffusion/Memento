namespace Memento.Core.Library;

/// <summary>
/// The second WebView2 virtual host, mapped by the app to the library folder, so the page streams media and peaks
/// with plain <c>&lt;audio&gt;</c> and <c>fetch</c>. The UI never builds these URLs; the host returns them.
/// </summary>
public static class LibraryUrls
{
    public const string VirtualHost = "library.memento";
    public const string Origin = "https://" + VirtualHost + "/";

    /// <summary><c>https://library.memento/projects/&lt;id&gt;/&lt;relative file&gt;</c>, each segment escaped.</summary>
    public static string ForProjectFile(string recordingId, string relativeFile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recordingId);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativeFile);
        var segments = relativeFile.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).Select(Uri.EscapeDataString);
        return $"{Origin}projects/{Uri.EscapeDataString(recordingId)}/{string.Join('/', segments)}";
    }
}
