using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Library;

/// <summary>
/// Library folders (2.1, DESIGN.md §6.5): a nested tree beside the timeline and type filters. A recording is filed in
/// one folder or the root through <c>project.json</c>'s <c>folder</c>; the tree is <c>folders.json</c>
/// (<see cref="FolderStore"/>) together with every folder a manifest names. Folders are data only: creating, renaming,
/// moving or deleting one, or moving a recording, never moves or deletes a file. Renames and moves rewrite the
/// manifests first and the folder list last, so an interruption leaves every recording in a folder that is shown.
/// Deleting a folder files what it held (recordings and folders) in its parent; no recording is ever deleted here.
/// </summary>
public sealed partial class FolderService(
    IProjectStore store,
    ProjectCatalog catalog,
    ILibraryIndex index,
    FolderStore folders,
    RecordingCoordinator recordings,
    ILogger<FolderService> logger) : IDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ILogger<FolderService> _logger = logger;

    public void Dispose() => _gate.Dispose();

    /// <summary><c>folders.list</c>: every folder with the recordings filed directly in it and in it with its subfolders.</summary>
    public async Task<FoldersListResult> ListAsync(CancellationToken cancellationToken)
    {
        var counts = await index.CountByFolderAsync(cancellationToken);
        var saved = await folders.LoadAsync(cancellationToken);
        var all = FolderStore.Complete(saved.Concat(counts.Keys));
        var direct = new Dictionary<string, int>(FolderPaths.Comparer);
        var root = 0;
        foreach (var (folder, count) in counts)
        {
            if (FolderPaths.TryNormalize(folder, out var path))
            {
                direct[path] = direct.GetValueOrDefault(path) + count;
            }
            else
            {
                root += count;
            }
        }

        var list = all
            .Order(FolderPaths.Comparer)
            .Select(path => new FolderInfo(
                path,
                FolderPaths.Name(path),
                FolderPaths.Parent(path),
                direct.GetValueOrDefault(path),
                direct.Where(kv => FolderPaths.IsWithin(kv.Key, path)).Sum(kv => kv.Value)))
            .ToList();
        return new FoldersListResult(list, root);
    }

    /// <summary><c>folders.create</c>: a new empty folder in <paramref name="parent"/> (<c>null</c>: the root).</summary>
    public async Task<string> CreateAsync(string? parent, string name, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tree = await TreeAsync(cancellationToken);
            var parentPath = parent is null ? null : Existing(tree, parent);
            var trimmed = ValidName(name);
            var path = FolderPaths.Combine(parentPath, trimmed);
            if (FolderPaths.Depth(path) > FolderPaths.MaxDepth)
            {
                throw new BridgeException(DomainErrorCodes.FolderInvalid, $"Folders can be nested {FolderPaths.MaxDepth} deep at most. Nothing was created; choose a folder higher up.", parentPath);
            }

            if (tree.Contains(path, FolderPaths.Comparer))
            {
                throw NameTaken(trimmed, parentPath);
            }

            await folders.SaveAsync(tree.Append(path), cancellationToken);
            catalog.NotifyChanged();
            LogCreated();
            return path;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary><c>folders.rename</c>: renames the folder in place; what it holds follows.</summary>
    public async Task<string> RenameAsync(string path, string name, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tree = await TreeAsync(cancellationToken);
            var from = Existing(tree, path);
            var trimmed = ValidName(name);
            var to = FolderPaths.Combine(FolderPaths.Parent(from), trimmed);
            if (!FolderPaths.Comparer.Equals(from, to) && tree.Contains(to, FolderPaths.Comparer))
            {
                throw NameTaken(trimmed, FolderPaths.Parent(from));
            }

            await MoveTreeAsync(tree, from, to, cancellationToken);
            return to;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary><c>folders.move</c>: moves the folder, with what it holds, into <paramref name="parent"/> (<c>null</c>: the root).</summary>
    public async Task<string> MoveAsync(string path, string? parent, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tree = await TreeAsync(cancellationToken);
            var from = Existing(tree, path);
            var parentPath = parent is null ? null : Existing(tree, parent);
            if (parentPath is not null && FolderPaths.IsWithin(parentPath, from))
            {
                throw new BridgeException(
                    DomainErrorCodes.FolderInvalid,
                    $"\"{FolderPaths.Name(from)}\" can't be moved into itself or one of its own folders. Nothing was moved.",
                    parentPath);
            }

            var to = FolderPaths.Combine(parentPath, FolderPaths.Name(from));
            if (FolderPaths.Comparer.Equals(from, to))
            {
                return from;
            }

            if (tree.Contains(to, FolderPaths.Comparer))
            {
                throw NameTaken(FolderPaths.Name(from), parentPath);
            }

            var deepest = tree.Where(f => FolderPaths.IsWithin(f, from)).Max(f => FolderPaths.Depth(f) - FolderPaths.Depth(from));
            if (FolderPaths.Depth(to) + deepest > FolderPaths.MaxDepth)
            {
                throw new BridgeException(DomainErrorCodes.FolderInvalid, $"Folders can be nested {FolderPaths.MaxDepth} deep at most, and this move would go deeper. Nothing was moved.", parentPath);
            }

            await MoveTreeAsync(tree, from, to, cancellationToken);
            return to;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// <c>folders.delete</c>: removes the folder; the recordings and folders in it move up into its parent (the root for a
    /// top-level folder). No recording is deleted. Returns how many recordings and folders moved up.
    /// </summary>
    public async Task<(int Recordings, int Folders)> DeleteAsync(string path, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tree = await TreeAsync(cancellationToken);
            var folder = Existing(tree, path);
            var parent = FolderPaths.Parent(folder);
            var moved = await RefileAsync(folder, f => FolderPaths.Comparer.Equals(f, folder) ? parent : FolderPaths.Rebase(f, folder, parent), cancellationToken);
            var children = tree.Count(f => FolderPaths.Comparer.Equals(FolderPaths.Parent(f), folder));
            var next = tree
                .Where(f => !FolderPaths.Comparer.Equals(f, folder))
                .Select(f => FolderPaths.IsWithin(f, folder) ? FolderPaths.Rebase(f, folder, parent)! : f);
            await folders.SaveAsync(next, cancellationToken);
            catalog.NotifyChanged();
            LogDeleted(moved, children);
            return (moved, children);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary><c>library.moveToFolder</c>: files the recordings in <paramref name="folder"/> (<c>null</c>: the root).</summary>
    public async Task<int> MoveRecordingsAsync(IReadOnlyList<string> recordingIds, string? folder, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var tree = await TreeAsync(cancellationToken);
            var target = folder is null ? null : Existing(tree, folder);
            var manifests = new List<ProjectManifest>();
            foreach (var id in recordingIds.Distinct(StringComparer.Ordinal))
            {
                ProjectManifest manifest;
                try
                {
                    manifest = ProjectId.IsValid(id) ? await store.LoadAsync(id, cancellationToken) : throw new ProjectNotFoundException(id);
                }
                catch (ProjectNotFoundException)
                {
                    throw ProjectService.NotFound(id);
                }

                if (recordings.IsBusy(id) || manifest.State is ProjectStates.Recording or ProjectStates.Finalizing)
                {
                    throw new BridgeException(
                        DomainErrorCodes.ProjectRecording,
                        $"\"{manifest.Details.Title}\" is still being recorded or saved. Nothing was moved; file it once it is in the Library.",
                        id);
                }

                manifests.Add(manifest);
            }

            var moved = 0;
            foreach (var manifest in manifests.Where(m => !SameFolder(m.Folder, target)))
            {
                await catalog.UpdateAsync(manifest.Id, m => m with { Folder = target }, cancellationToken);
                moved++;
            }

            return moved;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task MoveTreeAsync(IReadOnlyList<string> tree, string from, string to, CancellationToken cancellationToken)
    {
        var moved = await RefileAsync(from, f => FolderPaths.Rebase(f, from, to), cancellationToken);
        var next = tree.Select(f => FolderPaths.IsWithin(f, from) ? FolderPaths.Rebase(f, from, to)! : f).Append(to);
        await folders.SaveAsync(next, cancellationToken);
        catalog.NotifyChanged();
        LogMoved(moved);
    }

    /// <summary>Rewrites the folder of every recording filed in <paramref name="folder"/> or below it; returns how many.</summary>
    private async Task<int> RefileAsync(string folder, Func<string, string?> map, CancellationToken cancellationToken)
    {
        var count = 0;
        foreach (var id in store.ListIds())
        {
            ProjectManifest manifest;
            try
            {
                manifest = await store.LoadAsync(id, cancellationToken);
            }
            catch (Exception ex) when (ex is ProjectNotFoundException or ProjectSchemaException or IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (!FolderPaths.IsWithin(manifest.Folder, folder))
            {
                continue;
            }

            var next = map(manifest.Folder!);
            await catalog.UpdateAsync(id, m => m with { Folder = next }, cancellationToken);
            count++;
        }

        return count;
    }

    /// <summary>The folder tree as shown: <c>folders.json</c> and the folders the index knows recordings in.</summary>
    private async Task<IReadOnlyList<string>> TreeAsync(CancellationToken cancellationToken)
    {
        var counts = await index.CountByFolderAsync(cancellationToken);
        return new SortedSet<string>(FolderStore.Complete((await folders.LoadAsync(cancellationToken)).Concat(counts.Keys)), FolderPaths.Comparer).ToList();
    }

    private static string Existing(IReadOnlyList<string> tree, string path)
    {
        if (FolderPaths.TryNormalize(path, out var normalized) && tree.FirstOrDefault(f => FolderPaths.Comparer.Equals(f, normalized)) is { } found)
        {
            return found;
        }

        throw new BridgeException(DomainErrorCodes.FolderNotFound, $"There is no folder \"{path}\" in the Library any more. Nothing was changed; the Library shows the folders there are now.", path);
    }

    private static string ValidName(string name) =>
        FolderPaths.TryName(name, out var trimmed, out var problem)
            ? trimmed
            : throw new BridgeException(DomainErrorCodes.FolderInvalid, problem + " Nothing was changed.", name);

    private static BridgeException NameTaken(string name, string? parent) =>
        new(
            DomainErrorCodes.FolderNameTaken,
            parent is null
                ? $"There is already a folder called \"{name}\" in the Library. Nothing was changed; choose another name."
                : $"There is already a folder called \"{name}\" in \"{FolderPaths.Name(parent)}\". Nothing was changed; choose another name.",
            name);

    private static bool SameFolder(string? a, string? b) => a is null ? b is null : b is not null && FolderPaths.Comparer.Equals(a, b);

    [LoggerMessage(Level = LogLevel.Information, Message = "Library folder created")]
    private partial void LogCreated();

    [LoggerMessage(Level = LogLevel.Information, Message = "Library folder renamed or moved; {Recordings} recordings refiled")]
    private partial void LogMoved(int recordings);

    [LoggerMessage(Level = LogLevel.Information, Message = "Library folder deleted; {Recordings} recordings and {Folders} folders moved up")]
    private partial void LogDeleted(int recordings, int folders);
}
