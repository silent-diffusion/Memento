using System.Buffers.Binary;

namespace Memento.Video.Recovery;

/// <summary>
/// What an MP4 file holds, read from its boxes: whether it is fragmented, its fragments with their start times and
/// lengths, the complete-data length, and a torn tail if the writer died mid-fragment.
/// </summary>
/// <param name="IsFragmented">Has <c>moov/mvex</c> and <c>moof</c> fragments (what <see cref="Encoding.Mp4TrackWriter"/> writes).</param>
/// <param name="Timescale">Ticks per second of the first track's media time.</param>
/// <param name="Fragments">Each complete fragment (a <c>moof</c> followed by its <c>mdat</c>).</param>
/// <param name="ValidLength">Bytes up to the end of the last complete box (or fragment pair).</param>
/// <param name="TornBytes">Bytes after <see cref="ValidLength"/>: an incomplete box from a crash.</param>
/// <param name="HasIndex">Ends with an <c>mfra</c> random-access index (written by a clean finalize).</param>
public sealed record Mp4FileSummary(
    bool IsFragmented,
    uint Timescale,
    IReadOnlyList<Mp4Fragment> Fragments,
    long ValidLength,
    long TornBytes,
    bool HasMoov,
    bool HasIndex,
    long Samples,
    double DurationSeconds)
{
    /// <summary>Reads <paramref name="path"/>'s structure (sharing it with a writer that may still have it open).</summary>
    public static Mp4FileSummary Read(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Read(stream);
    }

    public static Mp4FileSummary Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var (boxes, torn) = Mp4BoxReader.TopLevel(stream);
        var moov = boxes.FirstOrDefault(b => b.Type == "moov");
        var hasMoov = moov.Type is not null;
        uint timescale = 0;
        uint trexDefaultDuration = 0;
        var fragmented = false;
        long moovSamples = 0;
        double moovDuration = 0;
        if (hasMoov)
        {
            var children = Mp4BoxReader.Children(stream, moov);
            fragmented = children.Any(c => c.Type == "mvex");
            var trak = children.FirstOrDefault(c => c.Type == "trak");
            if (trak.Type is not null)
            {
                timescale = ReadTimescale(stream, trak);
                (moovSamples, var moovTicks) = ReadSampleTable(stream, trak);
                moovDuration = timescale == 0 ? 0 : moovTicks / (double)timescale;
            }

            var mvex = children.FirstOrDefault(c => c.Type == "mvex");
            if (mvex.Type is not null)
            {
                var trex = Mp4BoxReader.Children(stream, mvex).FirstOrDefault(c => c.Type == "trex");
                if (trex.Type is not null)
                {
                    var payload = Mp4BoxReader.ReadPayload(stream, trex);
                    trexDefaultDuration = payload.Length >= 16 ? BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(12)) : 0;
                }
            }
        }

        var fragments = new List<Mp4Fragment>();
        long validLength = 0;
        for (var i = 0; i < boxes.Count; i++)
        {
            var box = boxes[i];
            if (box.Type == "moof")
            {
                // A fragment counts only with its mdat; a moof whose mdat was never completed is part of the torn tail.
                if (i + 1 >= boxes.Count || boxes[i + 1].Type != "mdat")
                {
                    break;
                }

                var implied = fragments.Count == 0 ? 0 : fragments[^1].DecodeTime + fragments[^1].Duration;
                fragments.Add(ReadFragment(stream, box, boxes[i + 1], trexDefaultDuration, implied));
                validLength = boxes[i + 1].End;
                i++;
                continue;
            }

            validLength = box.End;
        }

        var fileLength = stream.Length;
        var samples = fragmented ? fragments.Sum(f => (long)f.Samples) : moovSamples;
        var duration = fragmented
            ? (timescale == 0 || fragments.Count == 0 ? 0 : (fragments[^1].DecodeTime + fragments[^1].Duration - fragments[0].DecodeTime) / (double)timescale)
            : moovDuration;
        return new Mp4FileSummary(fragmented, timescale, fragments, validLength, fileLength - validLength, hasMoov, boxes.Any(b => b.Type == "mfra"), samples, duration);
    }

    private static uint ReadTimescale(Stream stream, Mp4Box trak)
    {
        var mdia = Mp4BoxReader.Children(stream, trak).FirstOrDefault(c => c.Type == "mdia");
        if (mdia.Type is null)
        {
            return 0;
        }

        var mdhd = Mp4BoxReader.Children(stream, mdia).FirstOrDefault(c => c.Type == "mdhd");
        if (mdhd.Type is null)
        {
            return 0;
        }

        var payload = Mp4BoxReader.ReadPayload(stream, mdhd);
        // version 1 has 64-bit creation/modification times.
        return payload[0] == 1 ? BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(20)) : BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(12));
    }

    private static (long Samples, long Ticks) ReadSampleTable(Stream stream, Mp4Box trak)
    {
        var mdia = Mp4BoxReader.Children(stream, trak).FirstOrDefault(c => c.Type == "mdia");
        var minf = mdia.Type is null ? default : Mp4BoxReader.Children(stream, mdia).FirstOrDefault(c => c.Type == "minf");
        var stbl = minf.Type is null ? default : Mp4BoxReader.Children(stream, minf).FirstOrDefault(c => c.Type == "stbl");
        var stts = stbl.Type is null ? default : Mp4BoxReader.Children(stream, stbl).FirstOrDefault(c => c.Type == "stts");
        if (stts.Type is null)
        {
            return (0, 0);
        }

        var payload = Mp4BoxReader.ReadPayload(stream, stts);
        var entries = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(4));
        long samples = 0;
        long ticks = 0;
        for (var i = 0; i < entries && 8 + (i * 8) + 8 <= payload.Length; i++)
        {
            var count = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(8 + (i * 8)));
            var delta = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(12 + (i * 8)));
            samples += count;
            ticks += (long)count * delta;
        }

        return (samples, ticks);
    }

    private static Mp4Fragment ReadFragment(Stream stream, Mp4Box moof, Mp4Box mdat, uint trexDefaultDuration, long impliedDecodeTime)
    {
        var traf = Mp4BoxReader.Children(stream, moof).FirstOrDefault(c => c.Type == "traf");
        // Without a tfdt box (Media Foundation writes none) a fragment starts where the previous one ended.
        var decodeTime = (ulong)impliedDecodeTime;
        uint samples = 0;
        long duration = 0;
        if (traf.Type is not null)
        {
            uint defaultDuration = trexDefaultDuration;
            foreach (var child in Mp4BoxReader.Children(stream, traf))
            {
                var payload = Mp4BoxReader.ReadPayload(stream, child, 1 << 20);
                switch (child.Type)
                {
                    case "tfhd":
                        defaultDuration = TfhdDefaultDuration(payload, defaultDuration);
                        break;
                    case "tfdt":
                        decodeTime = payload[0] == 1 ? BinaryPrimitives.ReadUInt64BigEndian(payload.AsSpan(4)) : BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(4));
                        break;
                    case "trun":
                        var (count, total) = TrunDuration(payload, defaultDuration);
                        samples += count;
                        duration += total;
                        break;
                }
            }
        }

        return new Mp4Fragment(moof.Offset, mdat.End - moof.Offset, (long)decodeTime, duration, samples);
    }

    private static uint TfhdDefaultDuration(byte[] payload, uint fallback)
    {
        var flags = BinaryPrimitives.ReadUInt32BigEndian(payload) & 0xFFFFFF;
        var offset = 8; // version/flags + track_ID
        if ((flags & 0x1) != 0)
        {
            offset += 8; // base_data_offset
        }

        if ((flags & 0x2) != 0)
        {
            offset += 4; // sample_description_index
        }

        return (flags & 0x8) != 0 ? BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset)) : fallback;
    }

    private static (uint Count, long Duration) TrunDuration(byte[] payload, uint defaultDuration)
    {
        var flags = BinaryPrimitives.ReadUInt32BigEndian(payload) & 0xFFFFFF;
        var count = BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(4));
        var offset = 8;
        if ((flags & 0x1) != 0)
        {
            offset += 4; // data_offset
        }

        if ((flags & 0x4) != 0)
        {
            offset += 4; // first_sample_flags
        }

        var hasDuration = (flags & 0x100) != 0;
        var perSample = (hasDuration ? 4 : 0) + ((flags & 0x200) != 0 ? 4 : 0) + ((flags & 0x400) != 0 ? 4 : 0) + ((flags & 0x800) != 0 ? 4 : 0);
        if (!hasDuration)
        {
            return (count, (long)count * defaultDuration);
        }

        long total = 0;
        for (var i = 0; i < count && offset + (i * perSample) + 4 <= payload.Length; i++)
        {
            total += BinaryPrimitives.ReadUInt32BigEndian(payload.AsSpan(offset + (i * perSample)));
        }

        return (count, total);
    }
}
