namespace Memento.Tools.VideoCheck;

/// <summary>
/// A generated audio track for the sync check: 16-bit mono PCM on the recording timeline, silent except for a 5 ms click
/// at each given time. Written to disk (never played) and read back by onset detection, so the audio side of the
/// comparison goes through a file like a real track does.
/// </summary>
internal static class SyntheticClicks
{
    public static void Write(string path, IReadOnlyList<double> clickSeconds, int rate)
    {
        var length = (int)(((clickSeconds.Count == 0 ? 0 : clickSeconds[^1]) + 1) * rate);
        var samples = new short[length];
        foreach (var at in clickSeconds)
        {
            var start = (int)Math.Round(at * rate);
            for (var i = start; i < Math.Min(length, start + (rate / 200)); i++)
            {
                samples[i] = 12_000;
            }
        }

        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write("RIFF"u8);
        writer.Write(36 + (length * 2));
        writer.Write("WAVEfmt "u8);
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(rate);
        writer.Write(rate * 2);
        writer.Write((short)2);
        writer.Write((short)16);
        writer.Write("data"u8);
        writer.Write(length * 2);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }
    }

    /// <summary>Click onsets (seconds) in a file written by <see cref="Write"/>.</summary>
    public static List<double> Find(string path)
    {
        using var stream = File.OpenRead(path);
        using var reader = new BinaryReader(stream);
        stream.Position = 24;
        var rate = reader.ReadInt32();
        stream.Position = 44;
        var result = new List<double>();
        var quiet = rate;
        for (long i = 0; stream.Position < stream.Length; i++)
        {
            var loud = Math.Abs((int)reader.ReadInt16()) > 3000;
            if (loud && quiet > rate / 2)
            {
                result.Add(i / (double)rate);
            }

            quiet = loud ? 0 : quiet + 1;
        }

        return result;
    }
}
