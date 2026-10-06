namespace Memento.Core.Host;

/// <summary>Reads free disk space.</summary>
public interface IFreeSpaceProbe
{
    /// <summary>Bytes available to the current user on the drive holding <paramref name="path"/>, or <c>null</c> if unreadable.</summary>
    long? GetFreeBytes(string path);
}
