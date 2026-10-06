namespace Memento.Core.Host;

/// <summary><see cref="IFreeSpaceProbe"/> backed by <see cref="DriveInfo"/>. The folder itself need not exist yet.</summary>
public sealed class DriveFreeSpaceProbe : IFreeSpaceProbe
{
    public long? GetFreeBytes(string path)
    {
        try
        {
            var root = Path.GetPathRoot(Path.GetFullPath(path));
            if (string.IsNullOrEmpty(root))
            {
                return null;
            }

            var drive = new DriveInfo(root);
            return drive.IsReady ? drive.AvailableFreeSpace : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }
}
