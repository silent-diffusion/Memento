using System.Globalization;

namespace Memento.Audio.Writing;

/// <summary>
/// The parts of one recorded track in order (<c>mic.wav</c>, <c>mic.part2.wav</c>, …), read as one stream.
/// </summary>
public sealed class WavTrackSet
{
    private WavTrackSet(IReadOnlyList<WavFileInfo> parts)
    {
        Parts = parts;
        Format = parts[0].Format;
        TotalFrames = parts.Sum(p => p.Frames);
        TotalDataBytes = parts.Sum(p => p.DataBytes);
    }

    public IReadOnlyList<WavFileInfo> Parts { get; }

    public AudioFormat Format { get; }

    public long TotalFrames { get; }

    public long TotalDataBytes { get; }

    public TimeSpan Duration => Format.DurationOf(TotalFrames);

    /// <summary>File name of part <paramref name="index"/> (1-based) of the track <paramref name="stem"/>.</summary>
    public static string PartFileName(string stem, int index)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stem);
        ArgumentOutOfRangeException.ThrowIfLessThan(index, 1);
        return index == 1 ? $"{stem}.wav" : string.Create(CultureInfo.InvariantCulture, $"{stem}.part{index}.wav");
    }

    /// <summary>Existing part files of <paramref name="stem"/> in <paramref name="directory"/>, in order, stopping at the first gap.</summary>
    public static IReadOnlyList<string> FindParts(string directory, string stem)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var parts = new List<string>();
        for (var i = 1; ; i++)
        {
            var path = System.IO.Path.Combine(directory, PartFileName(stem, i));
            if (!File.Exists(path))
            {
                return parts;
            }

            parts.Add(path);
        }
    }

    public static WavTrackSet Open(string directory, string stem)
    {
        var parts = FindParts(directory, stem);
        if (parts.Count == 0)
        {
            throw new FileNotFoundException($"No WAV parts for track '{stem}' in the tracks folder.", System.IO.Path.Combine(directory, PartFileName(stem, 1)));
        }

        return FromParts(parts);
    }

    public static WavTrackSet FromParts(IEnumerable<string> partPaths)
    {
        ArgumentNullException.ThrowIfNull(partPaths);
        var parts = partPaths.Select(WavFileInfo.Read).ToList();
        if (parts.Count == 0)
        {
            throw new ArgumentException("A track needs at least one WAV part.", nameof(partPaths));
        }

        foreach (var part in parts.Skip(1))
        {
            if (part.Format != parts[0].Format)
            {
                throw new InvalidDataException($"{System.IO.Path.GetFileName(part.Path)} is {part.Format} but the first part is {parts[0].Format}; parts of one track must match.");
            }
        }

        return new WavTrackSet(parts);
    }

    /// <summary>Opens all parts as one continuous PCM stream.</summary>
    public WavTrackSetReader OpenReader() => new(this);
}
