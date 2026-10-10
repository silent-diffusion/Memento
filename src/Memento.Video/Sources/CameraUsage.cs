using System.Diagnostics;
using Microsoft.Win32;

namespace Memento.Video.Sources;

/// <summary>
/// Which other app holds the camera, for "Logitech Brio is in use by Zoom." (DESIGN.md §18.6). Windows records each
/// app's camera use under the capability access manager's consent store: an entry whose <c>LastUsedTimeStop</c> is 0
/// while <c>LastUsedTimeStart</c> is set is using the camera now (the same data Settings' "recent activity" shows).
/// Desktop apps are keyed by their path with <c>#</c> for <c>\</c>; packaged apps by package family name.
/// </summary>
public static class CameraUsage
{
    /// <summary>The name of an app using a camera now, other than <paramref name="ownExecutablePath"/>; null when none is recorded.</summary>
    public static string? AppUsingCamera(string? ownExecutablePath = null)
    {
        var entries = new List<(string Key, long Start, long Stop)>();
        try
        {
            using var store = Registry.CurrentUser.OpenSubKey(CameraPrivacy.ConsentStore);
            if (store is null)
            {
                return null;
            }

            Collect(store, entries, packaged: true);
            using var nonPackaged = store.OpenSubKey("NonPackaged");
            if (nonPackaged is not null)
            {
                Collect(nonPackaged, entries, packaged: false);
            }
        }
        catch (System.Security.SecurityException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return FindActive(entries, ownExecutablePath);
    }

    /// <summary>Picks the most recently started entry that has not stopped and is not Memento. Pure, for tests.</summary>
    public static string? FindActive(IEnumerable<(string Key, long Start, long Stop)> entries, string? ownExecutablePath)
    {
        ArgumentNullException.ThrowIfNull(entries);
        var own = ownExecutablePath?.Replace('\\', '#');
        var active = entries
            .Where(e => e.Start > 0 && e.Stop == 0)
            .Where(e => own is null || !string.Equals(e.Key, own, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(e => e.Start)
            .FirstOrDefault();
        return active.Key is null ? null : DisplayName(active.Key);
    }

    /// <summary>"C:#Program Files#Zoom#bin#Zoom.exe" → "Zoom"; "Microsoft.WindowsCamera_8wekyb3d8bbwe" → "Microsoft.WindowsCamera".</summary>
    public static string DisplayName(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Contains('#', StringComparison.Ordinal))
        {
            var path = key.Replace('#', '\\');
            try
            {
                var description = File.Exists(path) ? FileVersionInfo.GetVersionInfo(path).FileDescription : null;
                if (!string.IsNullOrWhiteSpace(description))
                {
                    return description.Trim();
                }
            }
            catch (IOException)
            {
                // Fall back to the file name.
            }

            return Path.GetFileNameWithoutExtension(path);
        }

        var underscore = key.LastIndexOf('_');
        return underscore > 0 ? key[..underscore] : key;
    }

    private static void Collect(RegistryKey parent, List<(string Key, long Start, long Stop)> into, bool packaged)
    {
        foreach (var name in parent.GetSubKeyNames())
        {
            if (packaged && string.Equals(name, "NonPackaged", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            using var key = parent.OpenSubKey(name);
            if (key is null)
            {
                continue;
            }

            var start = key.GetValue("LastUsedTimeStart") is long s ? s : 0;
            var stop = key.GetValue("LastUsedTimeStop") is long t ? t : 0;
            into.Add((name, start, stop));
        }
    }
}
