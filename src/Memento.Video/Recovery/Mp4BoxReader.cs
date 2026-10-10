using System.Buffers.Binary;

namespace Memento.Video.Recovery;

/// <summary>
/// Reads the box structure of an MP4 file without decoding anything: the top-level boxes, and inside a fragmented file
/// each fragment's decode time and sample durations. Stops at the first box that runs past the end of the file (a
/// crash's torn tail) and reports it.
/// </summary>
public static class Mp4BoxReader
{
    /// <summary>The complete top-level boxes and, if the file ends inside a box, that torn box.</summary>
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

            boxes.Add(box.Value);
            offset = box.Value.End;
        }

        return (boxes, null);
    }

    /// <summary>The children of <paramref name="parent"/> (a container box such as moov, trak, moof, traf).</summary>
    public static List<Mp4Box> Children(Stream stream, Mp4Box parent, int skip = 0)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var children = new List<Mp4Box>();
        var offset = parent.PayloadOffset + skip;
        while (offset < parent.End)
        {
            var box = ReadHeader(stream, offset, parent.End);
            if (box is null || box.Value.End > parent.End || box.Value.Size < box.Value.HeaderSize)
            {
                break;
            }

            children.Add(box.Value);
            offset = box.Value.End;
        }

        return children;
    }

    private static Mp4Box? ReadHeader(Stream stream, long offset, long limit)
    {
        if (limit - offset < 8)
        {
            return null;
        }

        Span<byte> header = stackalloc byte[16];
        stream.Position = offset;
        stream.ReadExactly(header[..8]);
        long size = BinaryPrimitives.ReadUInt32BigEndian(header);
        var type = System.Text.Encoding.ASCII.GetString(header.Slice(4, 4));
        var headerSize = 8;
        if (size == 1)
        {
            if (limit - offset < 16)
            {
                return null;
            }

            stream.ReadExactly(header.Slice(8, 8));
            size = (long)BinaryPrimitives.ReadUInt64BigEndian(header[8..]);
            headerSize = 16;
        }
        else if (size == 0)
        {
            // "Extends to the end of the file": only valid for the last box; a writer that died never wrote the size.
            size = long.MaxValue / 2;
        }

        return new Mp4Box(type, offset, size, headerSize);
    }

    internal static byte[] ReadPayload(Stream stream, Mp4Box box, int max = 1 << 24)
    {
        var length = (int)Math.Min(box.PayloadSize, max);
        var buffer = new byte[length];
        stream.Position = box.PayloadOffset;
        stream.ReadExactly(buffer);
        return buffer;
    }
}
