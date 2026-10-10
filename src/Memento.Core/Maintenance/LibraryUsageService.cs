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
        long separateBytes = 0;
        var separateRecordings = 0;
        var mixOnly = 0;
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long size;
            try
            {
                size = store.GetSizeBytes(id);
                var manifest = await store.LoadAsync(id, cancellationToken);
                var tracks = SeparateTrackBytes(store.GetProjectFolder(id), manifest);
                separateBytes += tracks;
                separateRecordings += tracks > 0 ? 1 : 0;
                mixOnly += manifest.MixOnly is not null ? 1 : 0;
            }
            catch (Exception ex) when (ex is ProjectNotFoundException or ProjectSchemaException or IOException or System.Text.Json.JsonException)
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

        return new LibraryUsage(total, freeSpace.GetFreeBytes(root) ?? 0, ids.Count, largest, separateBytes, separateRecordings, mixOnly);
    }

    /// <summary>
    /// The size of a stored recording's separate track files that "Keep only the mix" would remove (2.0): 0 while it is
    /// not stored, has no mix, or already keeps only its mix. Only files the manifest names inside the folder count.
    /// </summary>
    public static long SeparateTrackBytes(string folder, ProjectManifest manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.MixOnly is not null || manifest.Mix is null || manifest.State is not (ProjectStates.Ready or ProjectStates.Recovered))
        {
            return 0;
        }

        long total = 0;
        foreach (var track in manifest.Tracks.Where(t => ProjectPaths.IsSafeRelative(t.File)))
        {
            var files = track.Codec == Audio.PassThroughWavEncoder.WavCodec ? Recording.CaptureParts.Find(folder, track.File) : [track.File];
            foreach (var file in files)
            {
                try
                {
                    var info = new FileInfo(ProjectPaths.Resolve(folder, file));
                    total += info.Exists ? info.Length : 0;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidDataException)
                {
                    // A name outside the folder, or a file Windows will not describe: not counted.
                }
            }
        }

        return total;
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
