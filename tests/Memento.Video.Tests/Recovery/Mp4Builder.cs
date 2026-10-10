using System.Buffers.Binary;

namespace Memento.Video.Tests.Recovery;

/// <summary>
/// Builds the box skeleton of a fragmented MP4 like the one Media Foundation's FMPEG4 sink writes (moov with mvex/trex,
/// then moof(tfhd, trun)/mdat pairs, no tfdt), with dummy sample bytes. Only the structure the reader looks at is real.
/// </summary>
internal static class Mp4Builder
{
    public const uint Timescale = 30_000;
    public const uint FrameDuration = 1_000;

    public static byte[] Box(string type, params byte[][] children)
    {
        var payload = children.SelectMany(c => c).ToArray();
        var box = new byte[8 + payload.Length];
        BinaryPrimitives.WriteUInt32BigEndian(box, (uint)box.Length);
        System.Text.Encoding.ASCII.GetBytes(type).CopyTo(box, 4);
        payload.CopyTo(box, 8);
        return box;
    }

    private static byte[] U32(params uint[] values)
    {
        var bytes = new byte[values.Length * 4];
        for (var i = 0; i < values.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(i * 4), values[i]);
        }

        return bytes;
    }

    public static byte[] Header()
    {
        var ftyp = Box("ftyp", System.Text.Encoding.ASCII.GetBytes("isom"), U32(0x200));
        var mdhd = Box("mdhd", U32(0, 0, 0, Timescale, 0, 0));
        var trak = Box("trak", Box("mdia", mdhd));
        var trex = Box("trex", U32(0, 1, 1, FrameDuration, 0, 0));
        var moov = Box("moov", trak, Box("mvex", trex));
        return [.. ftyp, .. moov];
    }

    /// <summary>A moof with <paramref name="samples"/> samples (durations in the trun) and its mdat.</summary>
    public static byte[] Fragment(int samples, uint duration = FrameDuration)
    {
        var tfhd = Box("tfhd", U32(0, 1));
        var entries = new List<uint> { 0x000101, (uint)samples, 0 };
        for (var i = 0; i < samples; i++)
        {
            entries.Add(duration);
            entries.Add(100);
        }

        // flags 0x000301: data offset, sample duration, sample size.
        entries[0] = 0x000301;
        var trun = Box("trun", U32([.. entries]));
        var moof = Box("moof", Box("traf", tfhd, trun));
        var mdat = Box("mdat", new byte[samples * 100]);
        return [.. moof, .. mdat];
    }

    public static byte[] File(int fragments, int samplesPerFragment = 10, bool index = false)
    {
        var bytes = new List<byte>(Header());
        for (var i = 0; i < fragments; i++)
        {
            bytes.AddRange(Fragment(samplesPerFragment));
        }

        if (index)
        {
            bytes.AddRange(Box("mfra", Box("mfro", U32(0, 16))));
        }

        return [.. bytes];
    }
}
