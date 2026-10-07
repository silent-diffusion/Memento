using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Projects;

namespace Memento.Core.Maintenance;

/// <summary><c>library.usage</c> and <c>library.rebuildIndex</c> (Settings › Storage and history).</summary>
public sealed class LibraryUsageService(IProjectStore store, ILibraryLocation library, IFreeSpaceProbe freeSpace, ILibraryIndex index, ProjectCatalog catalog)
{
    public async Task<LibraryUsage> UsageAsync(CancellationToken cancellationToken)
    {
        var root = library.Root;
        var total = await Task.Run(() => FolderSize(root), cancellationToken);
        var ids = store.ListIds();
        LibraryUsageLargest? largest = null;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long size;
            try
            {
                size = store.GetSizeBytes(id);
            }
            catch (ProjectNotFoundException)
            {
                continue;
            }

            if (largest is null || size > largest.SizeBytes)
            {
                largest = new LibraryUsageLargest(id, string.Empty, size);
            }
        }

        if (largest is not null)
        {
            try
            {
                largest = largest with { Title = (await store.LoadAsync(largest.RecordingId, cancellationToken)).Details.Title };
            }
            catch (ProjectNotFoundException)
            {
                largest = largest with { Title = largest.RecordingId };
            }
        }

        return new LibraryUsage(total, freeSpace.GetFreeBytes(root) ?? 0, ids.Count, largest);
    }

    public async Task<int> RebuildIndexAsync(CancellationToken cancellationToken)
    {
        var count = await index.RebuildAsync(cancellationToken);
        catalog.NotifyChanged([.. store.ListIds()]);
        return count;
    }

    internal static long FolderSize(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return 0;
        }

        long total = 0;
        foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint }))
        {
            total += file.Length;
        }

        return total;
    }
}
