using System.Globalization;

namespace Memento.Core.Updates;

/// <summary>Which releases a running version may update to.</summary>
public static class UpdatePolicy
{
    /// <summary>The first version published as a full release (not a pre-release); 0.5.0 was folded into 1.0.0, the first public release.</summary>
    public static readonly Version FirstFullRelease = new(0, 5, 0);

    /// <summary>
    /// Pre-releases are offered only to a build that is itself a pre-release: a SemVer with a suffix
    /// (<c>0.6.0-rc.1</c>) or a version before 0.5.0 (0.1–0.4 were published as pre-releases). From 0.5.0 on, a full
    /// release sees only full releases.
    /// </summary>
    public static bool AcceptsPreReleases(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        var core = version.Split('+', 2)[0];
        if (core.Contains('-', StringComparison.Ordinal))
        {
            return true;
        }

        var parts = core.Split('.');
        if (parts.Length < 2
            || !int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major)
            || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor))
        {
            // Unreadable: stay on full releases.
            return false;
        }

        var patch = parts.Length > 2 && int.TryParse(parts[2], NumberStyles.None, CultureInfo.InvariantCulture, out var p) ? p : 0;
        return new Version(major, minor, patch) < FirstFullRelease;
    }

    /// <summary>
    /// Whether the hidden <c>--update-feed</c> test switch may use <paramref name="feed"/>: <c>off</c>, a folder on this PC
    /// (a fully qualified path, not a network share), or an <c>https:</c> address on this PC. A plain <c>http:</c>
    /// address (still only on this PC) is accepted only when <paramref name="allowHttp"/> is set, which Debug builds do;
    /// Release builds never install an update fetched without TLS (security audit SA-39b), and a shortcut carrying the
    /// switch can never point Memento at someone else's server.
    /// </summary>
    public static bool IsAllowedFeed(string? feed, bool allowHttp)
    {
        if (string.IsNullOrWhiteSpace(feed))
        {
            return false;
        }

        if (string.Equals(feed, "off", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (feed.Contains("://", StringComparison.Ordinal))
        {
            return Uri.TryCreate(feed, UriKind.Absolute, out var uri)
                && uri.IsLoopback
                && (uri.Scheme == Uri.UriSchemeHttps || (allowHttp && uri.Scheme == Uri.UriSchemeHttp));
        }

        return Path.IsPathFullyQualified(feed)
            && !feed.StartsWith(@"\\", StringComparison.Ordinal)
            && !feed.StartsWith("//", StringComparison.Ordinal);
    }
}
