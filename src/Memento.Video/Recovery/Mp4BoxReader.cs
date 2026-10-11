using System.Buffers.Binary;

namespace Memento.Video.Recovery;

/// <summary>
/// Reads the box structure of an MP4 file without decoding anything: the top-level boxes, and inside a fragmented file
/// each fragment's decode time and sample durations. Stops at the first box that runs past the end of the file (a
/// crash's torn tail) and reports it.
/// </summary>
/// <remarks>
/// The file may come from a project folder copied in from elsewhere, so every size is untrusted (security audit 2,
/// SA2-01): a box never ends past its parent or the file (sizes that would overflow count as running past the end),
/// containers are read only to the fixed depth the callers ask for (no recursion), and the number of boxes is capped
/// (<see cref="MaxTopLevelBoxes"/>, <see cref="MaxChildren"/>), so a file of millions of empty boxes is refused with an
/// <see cref="InvalidDataException"/> instead of filling memory. A refusal never reports a torn tail, so nothing is cut.
/// </remarks>
public static class Mp4BoxReader
{
    /// <summary>
    /// Top-level boxes read at most: Media Foundation writes a <c>moof</c>/<c>mdat</c> pair every 10 frames, so 24 hours
    /// at 60 fps is about 1.04 million boxes.
    /// </summary>
    public const int MaxTopLevelBoxes = 2_000_000;

    /// <summary>Children of one container read at most (a recorded <c>moov</c> or <c>traf</c> has a handful).</summary>
    public const int MaxChildren = 65_536;

    /// <summary>The complete top-level boxes and, if the file ends inside a box, that torn box.</summary>
    /// <exception cref="InvalidDataException">The file holds more than <see cref="MaxTopLevelBoxes"/> boxes.</exception>
    public static (IReadOnlyList<Mp4Box> Boxes, Mp4Box? Torn) TopLevel(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var boxes = new List<Mp4Box>();
        var length = stream.Length;
        long offset = 0;
        while (offset < length)
        {
            var box = ReadHeader(stream, offset, length);
            if (box is null)
            {
                return (boxes, new Mp4Box("????", offset, length - offset, 0));
            }

            if (box.Value.End > length || box.Value.Size < box.Value.HeaderSize)
            {
                return (boxes, box);
            }

            if (boxes.Count == MaxTopLevelBoxes)
            {
                throw new InvalidDataException($"The video file holds more than {MaxTopLevelBoxes:N0} boxes, which no recording has; it was not read.");
            }

            boxes.Add(box.Value);
            offset = box.Value.End;
        }

        return (boxes, null);
    }

    /// <summary>The children of <paramref name="parent"/> (a container box such as moov, trak, moof, traf).</summary>
    /// <exception cref="InvalidDataException">The container holds more than <see cref="MaxChildren"/> boxes.</exception>
    public static List<Mp4Box> Children(Stream stream, Mp4Box parent, int skip = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var children = new List<Mp4Box>();
        if (skip < 0 || parent.Size < parent.HeaderSize || skip > parent.PayloadSize)
        {
            return children;
        }

        var offset = parent.PayloadOffset + skip;
        while (offset < parent.End)
        {
            var box = ReadHeader(stream, offset, parent.End);
            if (box is null || box.Value.End > parent.End || box.Value.Size < box.Value.HeaderSize)
            {
                break;
            }

            if (children.Count == MaxChildren)
            {
                throw new InvalidDataException($"A '{parent.Type}' box in the video file holds more than {MaxChildren:N0} boxes, which no recording has; it was not read.");
            }

            children.Add(box.Value);
            offset = box.Value.End;
        }

        return children;
    }

    private static Mp4Box? ReadHeader(Stream stream, long offset, long limit)
    {
        if (offset < 0 || limit - offset < 8)
        {
            return null;
        }

        Span<byte> header = stackalloc byte[16];
        stream.Position = offset;
        stream.ReadExactly(header[..8]);
        ulong size = BinaryPrimitives.ReadUInt32BigEndian(header);
        var type = TypeName(header.Slice(4, 4));
        var headerSize = 8;
        if (size == 1)
        {
            if (limit - offset < 16)
            {
                return null;
            }

            stream.ReadExactly(header.Slice(8, 8));
            size = BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
            headerSize = 16;
        }
        else if (size == 0)
        {
            // "Extends to the end of the file": only valid for the last box; a writer that died never wrote the size.
            size = ulong.MaxValue;
        }

        // A size past what a long can hold from here (a hostile 64-bit size, or size 0) runs past any end: the caller
        // sees End > limit, never a negative End that would move the reader backwards.
        var room = (ulong)(long.MaxValue - offset);
        return new Mp4Box(type, offset, size > room ? (long)room : (long)size, headerSize);
    }

    /// <summary>The box type, without a new string for the types a recording is made of.</summary>
    private static string TypeName(ReadOnlySpan<byte> type) =>
        BinaryPrimitives.ReadUInt32BigEndian(type) switch
        {
            0x6D6F6F66 => "moof",
            0x6D646174 => "mdat",
            0x6D6F6F76 => "moov",
            0x66747970 => "ftyp",
            0x66726565 => "free",
            0x6D667261 => "mfra",
            0x74726166 => "traf",
            0x74666864 => "tfhd",
            0x7472756E => "trun",
            0x74666474 => "tfdt",
            _ => System.Text.Encoding.ASCII.GetString(type),
        };

    /// <summary>
    /// The payload of <paramref name="box"/>, at most <paramref name="max"/> bytes. Callers check its length before
    /// reading a field: a damaged box can be shorter than its type requires.
    /// </summary>
    internal static byte[] ReadPayload(Stream stream, Mp4Box box, int max = 1 << 24)
    {
        var length = (int)Math.Clamp(box.PayloadSize, 0, max);
        var buffer = new byte[length];
        stream.Position = box.PayloadOffset;
        stream.ReadExactly(buffer);
        return buffer;
    }
}
