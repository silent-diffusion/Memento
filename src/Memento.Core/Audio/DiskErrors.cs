namespace Memento.Core.Audio;

/// <summary>Recognises "the drive is full" among I/O failures.</summary>
public static class DiskErrors
{
    private const int ErrorHandleDiskFull = 39;
    private const int ErrorDiskFull = 112;

    public static bool IsDiskFull(Exception exception) =>
        exception is IOException io && (io.HResult & 0xFFFF) is ErrorDiskFull or ErrorHandleDiskFull;

    /// <summary>An <see cref="IOException"/> carrying <c>ERROR_DISK_FULL</c>, as Windows raises it.</summary>
    public static IOException CreateDiskFull(string path) =>
        new($"There is not enough space on the disk to write {Path.GetFileName(path)}.", unchecked((int)0x80070070));
}
