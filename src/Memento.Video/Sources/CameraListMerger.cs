using System.Globalization;

namespace Memento.Video.Sources;

/// <summary>
/// Merges the Media Foundation cameras and the DirectShow video input devices into the one camera list the picker shows
/// (ENGINE-NOTES.md §R). A camera Windows knows as a camera appears in both lists; it is kept once, from Media
/// Foundation (the GPU path), matched by its device instance (the device path without its interface class) or, for a
/// DirectShow entry without a path, by its name. What only DirectShow lists (virtual and software cameras) follows, in
/// DirectShow's order. Pure, so the merge is unit-tested with made-up lists.
/// </summary>
public static class CameraListMerger
{
    /// <summary>The prefix of a DirectShow camera's id key: <c>camera:dshow:…</c>.</summary>
    public const string DirectShowPrefix = "dshow:";

    public static IReadOnlyList<VideoSourceInfo> Merge(IReadOnlyList<CameraDevice> mediaFoundation, IReadOnlyList<CameraDevice> directShow)
    {
        ArgumentNullException.ThrowIfNull(mediaFoundation);
        ArgumentNullException.ThrowIfNull(directShow);
        var result = new List<VideoSourceInfo>(mediaFoundation.Count + directShow.Count);
        var instances = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var camera in mediaFoundation)
        {
            if (!keys.Add(camera.Key))
            {
                continue;
            }

            result.Add(new VideoSourceInfo(VideoSourceId.Camera(camera.Key), camera.Name, "Camera", 0, 0, 0, false, false));
            if (Instance(camera.DevicePath ?? camera.Key) is { } instance)
            {
                instances.Add(instance);
            }

            names.Add(camera.Name.Trim());
        }

        foreach (var camera in directShow)
        {
            var key = camera.Key.StartsWith(DirectShowPrefix, StringComparison.Ordinal) ? camera.Key : DirectShowPrefix + camera.Key;
            var instance = Instance(camera.DevicePath);
            var duplicate = instance is not null ? instances.Contains(instance) : names.Contains(camera.Name.Trim());
            if (duplicate || !keys.Add(key))
            {
                continue;
            }

            result.Add(new VideoSourceInfo(VideoSourceId.Camera(key), camera.Name, "Camera", 0, 0, 0, false, false));
        }

        return result;
    }

    /// <summary>
    /// A DirectShow device's id key, stable while Memento runs: its device path when it has one, else its filter's CLSID
    /// (a virtual camera: <c>dshow:clsid:{A3FCE0F5-…}</c>), else its name; a repeat gets a counter (<c>#2</c>).
    /// <paramref name="seen"/> counts the keys given out so far in this enumeration.
    /// </summary>
    public static string DirectShowKey(string name, string? devicePath, string? clsid, IDictionary<string, int> seen)
    {
        ArgumentNullException.ThrowIfNull(name);
        ArgumentNullException.ThrowIfNull(seen);
        string key;
        if (!string.IsNullOrWhiteSpace(devicePath))
        {
            key = DirectShowPrefix + devicePath.Trim();
        }
        else if (!string.IsNullOrWhiteSpace(clsid) && Guid.TryParse(clsid, out var guid))
        {
            key = DirectShowPrefix + "clsid:" + guid.ToString("B").ToUpperInvariant();
        }
        else
        {
            key = DirectShowPrefix + "name:" + name.Trim();
        }

        var count = seen.TryGetValue(key, out var before) ? before + 1 : 1;
        seen[key] = count;
        return count == 1 ? key : key + "#" + count.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The device instance of a device interface path, lower-case and without the interface class and reference string:
    /// <c>\\?\usb#vid_046d&amp;pid_085e&amp;mi_00#7&amp;1a2b#{e5323777-…}\global</c> → <c>\\?\usb#vid_046d&amp;pid_085e&amp;mi_00#7&amp;1a2b</c>.
    /// Media Foundation and DirectShow give the same camera different interface classes. Null without a path.
    /// </summary>
    public static string? Instance(string? devicePath)
    {
        if (string.IsNullOrWhiteSpace(devicePath) || !devicePath.Trim().StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return null;
        }

        var lower = devicePath.Trim().ToLowerInvariant();
        var classStart = lower.LastIndexOf("#{", StringComparison.Ordinal);
        return classStart > 4 ? lower[..classStart] : lower;
    }
}
