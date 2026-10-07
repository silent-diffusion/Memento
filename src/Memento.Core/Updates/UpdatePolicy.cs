using System.Globalization;

namespace Memento.Core.Updates;

/// <summary>Which releases a running version may update to.</summary>
public static class UpdatePolicy
{
    /// <summary>The first full (not pre-release) version: 0.5.0, the first public release.</summary>
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
}
