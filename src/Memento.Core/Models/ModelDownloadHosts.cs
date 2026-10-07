namespace Memento.Core.Models;

/// <summary>
/// The servers models come from. A catalog address must be on one of the publishing hosts; a download may be sent on
/// (redirected) only to those hosts or to their file servers. Loopback addresses are accepted for tests, and a
/// download that started on loopback may only end on the same loopback host.
/// </summary>
public static class ModelDownloadHosts
{
    /// <summary>Hosts a catalog <c>url</c> may name.</summary>
    public static IReadOnlyList<string> CatalogHosts { get; } = ["huggingface.co", "github.com"];

    /// <summary>Hosts a download may end on, besides <see cref="CatalogHosts"/>; a leading dot means "any subdomain of".</summary>
    public static IReadOnlyList<string> FileHosts { get; } = [".huggingface.co", ".hf.co", ".githubusercontent.com"];

    /// <summary>Whether a catalog entry may name <paramref name="uri"/> as its download address.</summary>
    public static bool IsAllowedCatalogUrl(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        if (!uri.IsAbsoluteUri || !string.IsNullOrEmpty(uri.UserInfo))
        {
            return false;
        }

        return uri.IsLoopback
            ? uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps
            : uri.Scheme == Uri.UriSchemeHttps && CatalogHosts.Contains(uri.IdnHost, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>Whether a download that was asked of <paramref name="requested"/> may be read from <paramref name="answered"/>.</summary>
    public static bool IsAllowedDownload(Uri requested, Uri? answered)
    {
        ArgumentNullException.ThrowIfNull(requested);
        if (answered is null || !answered.IsAbsoluteUri)
        {
            return false;
        }

        if (requested.IsLoopback || answered.IsLoopback)
        {
            return requested.IsLoopback
                && answered.IsLoopback
                && string.Equals(requested.IdnHost, answered.IdnHost, StringComparison.OrdinalIgnoreCase)
                && requested.Port == answered.Port;
        }

        if (answered.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        var host = answered.IdnHost;
        return CatalogHosts.Contains(host, StringComparer.OrdinalIgnoreCase)
            || FileHosts.Any(suffix => host.Length > suffix.Length && host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }
}
