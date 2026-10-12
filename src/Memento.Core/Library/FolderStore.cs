using System.Text.Json;
using Memento.Core.Projects;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Library;

/// <summary>
/// <c>&lt;library&gt;\folders.json</c> (2.1): the Library's folder tree, so a folder with nothing in it yet survives a
/// restart and an index rebuild. The tree shown is this list together with every folder a recording's
/// <c>project.json</c> names (<see cref="FolderService"/>), so a lost or damaged file loses only empty folders.
/// Written atomically with a <c>schemaVersion</c>.
/// </summary>
public sealed partial class FolderStore(ILibraryLocation library, ILogger<FolderStore> logger)
{
    public const string FileName = "folders.json";

    private readonly ILogger<FolderStore> _logger = logger;

    public string FilePath => Path.Combine(library.Root, FileName);

    /// <summary>The saved folders, valid paths only, each once (without case), with every folder above them.</summary>
    public async Task<IReadOnlyList<string>> LoadAsync(CancellationToken cancellationToken)
    {
        FoldersDocument? document;
        try
        {
            document = await AtomicJsonFile.ReadAsync(FilePath, ProjectJsonContext.Default.FoldersDocument, cancellationToken);
        }
        catch (JsonException ex)
        {
            LogUnreadable(ex, FilePath);
            return [];
        }

        return Complete(document?.Folders ?? []);
    }

    public async Task SaveAsync(IEnumerable<string> folders, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(library.Root);
        var document = new FoldersDocument { Folders = Complete(folders).Order(FolderPaths.Comparer).ToList() };
        await AtomicJsonFile.WriteAsync(FilePath, document, ProjectJsonContext.Default.FoldersDocument, cancellationToken);
    }

    /// <summary>Valid paths only, each once, with their parents added.</summary>
    public static IReadOnlyList<string> Complete(IEnumerable<string?> folders)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(FolderPaths.Comparer);
        foreach (var folder in folders)
        {
            if (!FolderPaths.TryNormalize(folder, out var path))
            {
                continue;
            }

            foreach (var each in FolderPaths.SelfAndAncestors(path))
            {
                if (seen.Add(each))
                {
                    result.Add(each);
                }
            }
        }

        return result;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Path} could not be read; empty folders in it are not shown")]
    private partial void LogUnreadable(Exception exception, string path);
}
