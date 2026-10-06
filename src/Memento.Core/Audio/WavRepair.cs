namespace Memento.Core.Audio;

/// <summary>
/// Fixes a WAV file whose writer never finished: the RIFF and data sizes are rewritten from the file length,
/// and a trailing partial frame is cut. Samples are never changed. Safe to run on a file that is already valid.
/// </summary>
public static class WavRepair
{
    /// <summary>Repairs <paramref name="path"/> in place.</summary>
    /// <param name="knownFormat">
    /// The format the track was recorded in (from <c>recording.state.json</c>). When given, a file whose header never
    /// reached the disk is rebuilt with Memento's header layout; without it such a file is reported unrepairable.
    /// </param>
    public static WavRepairResult Repair(string path, PcmFormat? knownFormat = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            return WavRepairResult.Failed(path, "The track file is missing.");
        }

        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        WavInfo info;
        try
        {
            info = WavInfo.Read(stream);
        }
        catch (InvalidDataException ex)
        {
            if (knownFormat is null || stream.Length < WavLayout.HeaderSize)
            {
                return WavRepairResult.Failed(path, ex.Message);
            }

            // The header is damaged but the file is at least as long as ours: assume our layout.
            info = new WavInfo(knownFormat, WavLayout.HeaderSize, 0, IsRf64: false);
            stream.Seek(0, SeekOrigin.Begin);
            stream.Write(WavLayout.BuildHeader(knownFormat, 0));
        }

        var available = Math.Max(0, stream.Length - info.DataOffset);
        var whole = available - (available % info.Format.BlockAlign);
        var truncated = available - whole;
        if (truncated > 0)
        {
            stream.SetLength(info.DataOffset + whole);
        }

        var changed = truncated > 0 || whole != info.DeclaredDataBytes;
        if (changed)
        {
            if (info.DataOffset == WavLayout.HeaderSize)
            {
                stream.Seek(0, SeekOrigin.Begin);
                stream.Write(WavLayout.BuildHeader(info.Format, whole));
            }
            else
            {
                PatchForeignHeader(stream, info, whole);
            }

            stream.Flush(flushToDisk: true);
        }

        return new WavRepairResult(path, true, info.Format, whole, info.Format.BytesToMilliseconds(whole), changed, truncated, null);
    }

    /// <summary>A file with a different chunk layout (not written by Memento): patch the two 32-bit sizes only.</summary>
    private static void PatchForeignHeader(FileStream stream, WavInfo info, long dataBytes)
    {
        if (dataBytes > uint.MaxValue - info.DataOffset)
        {
            throw new InvalidDataException("The file is larger than a RIFF header can describe and has no RF64 space reserved.");
        }

        Span<byte> size = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)(info.DataOffset - 8 + dataBytes));
        stream.Seek(4, SeekOrigin.Begin);
        stream.Write(size);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)dataBytes);
        stream.Seek(info.DataOffset - 4, SeekOrigin.Begin);
        stream.Write(size);
    }
}
