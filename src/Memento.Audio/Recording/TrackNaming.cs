using System.Globalization;
using System.Text;
using Memento.Audio.Sources;
using Memento.Audio.Writing;

namespace Memento.Audio.Recording;

/// <summary>
/// File stems for tracks: <c>mic</c>, <c>system</c>, <c>app-&lt;process&gt;</c>, with <c>-2</c>, <c>-3</c>, … when a stem
/// is taken in this session or already has files in the folder (a source re-enabled mid-session gets a new file).
/// </summary>
public static class TrackNaming
{
    public static string BaseStem(AudioSourceId id, AudioSourceInfo? info) => id.Kind switch
    {
        AudioSourceKind.Microphone => "mic",
        AudioSourceKind.System => "system",
        _ => "app-" + Sanitize(info?.ProcessName ?? id.ProcessId.ToString(CultureInfo.InvariantCulture)),
    };

    public static string UniqueStem(string baseStem, string directory, ICollection<string> taken)
    {
        ArgumentNullException.ThrowIfNull(taken);
        for (var i = 1; ; i++)
        {
            var stem = i == 1 ? baseStem : string.Create(CultureInfo.InvariantCulture, $"{baseStem}-{i}");
            if (!taken.Contains(stem) && !File.Exists(Path.Combine(directory, WavTrackSet.PartFileName(stem, 1))))
            {
                return stem;
            }
        }
    }

    /// <summary>Lowercase ASCII letters, digits and dashes only.</summary>
    internal static string Sanitize(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var c in value.ToLowerInvariant())
        {
            if (c is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
            {
                sb.Append(c);
            }
            else if (sb.Length > 0 && sb[^1] != '-')
            {
                sb.Append('-');
            }
        }

        var s = sb.ToString().Trim('-');
        return s.Length == 0 ? "app" : s[..Math.Min(s.Length, 40)];
    }
}
