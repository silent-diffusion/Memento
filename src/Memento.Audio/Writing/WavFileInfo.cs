namespace Memento.Audio.Writing;

/// <summary>
/// Header facts of one WAV file. <see cref="DataBytes"/> is what is really readable: the declared size, or the
/// bytes on disk rounded down to whole frames when the header was never patched (a crash before a checkpoint).
/// </summary>
public sealed record WavFileInfo(
    string Path,
    AudioFormat Format,
    long DataOffset,
    long DataBytes,
    long DeclaredDataBytes,
    long FileLength)
{
    public long Frames => DataBytes / Format.BlockAlign;

    public TimeSpan Duration => Format.DurationOf(Frames);

    /// <summary>True when the header disagrees with the file length (run <see cref="StreamingWavWriter.Repair"/>).</summary>
    public bool HeaderNeedsRepair => DeclaredDataBytes != DataBytes || DataOffset + DataBytes != FileLength;

    public static WavFileInfo Read(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        var header = WavHeader.Parse(file, System.IO.Path.GetFileName(path));
        var available = Math.Max(0, file.Length - header.DataOffset);
        var declared = (long)header.DeclaredDataBytes;
        var usable = declared > 0 && declared <= available ? declared : available;
        usable -= usable % header.Format.BlockAlign;
        return new WavFileInfo(path, header.Format, header.DataOffset, usable, declared, file.Length);
    }
}
