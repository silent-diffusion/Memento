using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>library.processing</c>: the processing card. Its <c>stages</c> list every stage, finished <c>stored</c> and
/// <c>optimize</c> included; <c>meta</c> is the Library row. 2.0: a recording whose video stage failed stays the card's
/// recording (with <c>failure</c>, the §18.6 "Video encoding failed" card) until a remedy is chosen, even when nothing
/// else is active or queued.
/// </summary>
public sealed class LibraryProcessingMethod(ILibraryIndex index, LibraryAvailability? availability = null, IProjectStore? store = null) : BridgeMethod<EmptyParams, LibraryProcessingResult>
{
    public override string Name => BridgeMethodNames.LibraryProcessing;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<LibraryProcessingResult> ResultTypeInfo => BridgeJsonContext.Default.LibraryProcessingResult;

    public override async Task<LibraryProcessingResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        // A library that is not connected processes nothing; library.list says why.
        if (availability?.Unavailable is not null)
        {
            return new LibraryProcessingResult(null, 0);
        }

        var processing = await index.ListProcessingAsync(cancellationToken);
        var current = processing.Count > 0 ? processing[0] : await FailedVideoAsync(cancellationToken);
        if (current is null)
        {
            return new LibraryProcessingResult(null, 0);
        }

        return new LibraryProcessingResult(
            new ProcessingCurrent(current.Summary.Id, current.Summary.Title, current.Summary, current.Stages, await VideoFailureAsync(current, cancellationToken)),
            Math.Max(0, processing.Count - 1));
    }

    private static bool VideoFailed(IReadOnlyList<StageStatus> stages) =>
        stages.Any(s => s.Stage == StageNames.Video && s.State == StageStates.Failed);

    /// <summary>The newest recording whose video stage failed and waits for a remedy, or <c>null</c>.</summary>
    private async Task<ProcessingEntry?> FailedVideoAsync(CancellationToken cancellationToken)
    {
        if (store is null)
        {
            return null;
        }

        var all = await index.QueryAsync(LibraryQuery.All, cancellationToken);
        var failed = all.Recordings.FirstOrDefault(r => VideoFailed(r.Stages));
        if (failed is null)
        {
            return null;
        }

        try
        {
            var manifest = await store.LoadAsync(failed.Id, cancellationToken);
            return new ProcessingEntry(failed, manifest.Stages);
        }
        catch (ProjectNotFoundException)
        {
            return null;
        }
    }

    private async Task<StageFailure?> VideoFailureAsync(ProcessingEntry entry, CancellationToken cancellationToken)
    {
        if (store is null || !VideoFailed(entry.Stages))
        {
            return null;
        }

        try
        {
            var manifest = await store.LoadAsync(entry.Summary.Id, cancellationToken);
            return manifest.Failures.FirstOrDefault(f => f.Stage == StageNames.Video)?.ToContract();
        }
        catch (ProjectNotFoundException)
        {
            return null;
        }
    }
}
