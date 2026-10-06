using System.Globalization;

namespace Memento.Core.Recording;

/// <summary>
/// Capture WAVs of one track: classic RIFF stops at 4 GiB, so the engine rolls over at 3.5 GiB from
/// <c>tracks/mic.wav</c> to <c>tracks/mic.part2.wav</c>, <c>tracks/mic.part3.wav</c>, … (ARCHITECTURE.md §5.3).
/// The manifest and <c>recording.state.json</c> name the first part; the rest follow this convention, which must match
/// <c>Memento.Audio.Writing.WavTrackSet.PartFileName</c>.
/// </summary>
public static class CaptureParts
{
    /// <summary>Relative path of part <paramref name="index"/> (1-based) of the track whose first part is <paramref name="firstPart"/>.</summary>
    public static string PartPath(string firstPart, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firstPart);
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        var normalized = firstPart.Replace('\\', '/');
        if (index == 1)
        {
            return normalized;
        }

        var slash = normalized.LastIndexOf('/');
        var directory = slash >= 0 ? normalized[..(slash + 1)] : string.Empty;
        var stem = Path.GetFileNameWithoutExtension(normalized[(slash + 1)..]);
        return string.Create(CultureInfo.InvariantCulture, $"{directory}{stem}.part{index}.wav");
    }

    /// <summary>Relative paths of every part on disk, in order, stopping at the first missing one.</summary>
    public static IReadOnlyList<string> Find(string projectFolder, string firstPart)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        var parts = new List<string>();
        for (var i = 1; ; i++)
        {
            var relative = PartPath(firstPart, i);
            if (!File.Exists(Path.Combine(projectFolder, relative.Replace('/', Path.DirectorySeparatorChar))))
            {
                return parts;
            }

            parts.Add(relative);
        }
    }
}
