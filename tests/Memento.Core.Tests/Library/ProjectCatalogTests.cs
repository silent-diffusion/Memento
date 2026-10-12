using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Library;

public sealed class ProjectCatalogTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly ProjectStore _store;
    private readonly HeldIndex _index = new();
    private readonly ProjectCatalog _catalog;

    public ProjectCatalogTests()
    {
        _store = new ProjectStore(new FakeLibraryLocation(_directory.Path), TimeProvider.System, NullLogger<ProjectStore>.Instance);
        _catalog = new ProjectCatalog(_store, _index, new BridgeEventPublisher(new RecordingEventSink()));
    }

    public void Dispose()
    {
        _catalog.Dispose();
        _directory.Dispose();
    }

    [Fact]
    public async Task ARefreshThatReadTheManifestBeforeAWriteNeverPutsTheOlderOneBackInTheIndex()
    {
        var created = await _store.CreateAsync(new ProjectCreateRequest("Weekly sync", "meeting", DateTimeOffset.Now, ProjectStates.Ready), CancellationToken.None);

        // A stage's refresh has read the manifest and is slow to write the row (a starved machine) when the user's
        // change comes in. Ordered by signals, not by time.
        _index.HoldNext();
        var touched = _catalog.TouchedAsync(created.Id, CancellationToken.None);
        await _index.Held.WaitAsync(Patience.Ceiling);
        var renamed = _catalog.UpdateAsync(created.Id, m => m with { DurationMs = 4_242 }, CancellationToken.None);

        // The write waits for the refresh now, so it cannot be awaited here; give it a moment to overtake the refresh,
        // which it did before the fix. The wait decides only whether a regression shows, never whether this passes.
        await Task.WhenAny(renamed, Task.Delay(200));
        _index.Release();
        await Task.WhenAll(touched, renamed).WaitAsync(Patience.Ceiling);

        Assert.Equal(4_242, (await _store.LoadAsync(created.Id, CancellationToken.None)).DurationMs);
        Assert.Equal(4_242, _index.Last!.DurationMs);
    }

    /// <summary>Records the last indexed manifest; can hold one upsert until released.</summary>
    private sealed class HeldIndex : ILibraryIndex
    {
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _held = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _hold;

        public ProjectManifest? Last { get; private set; }

        public Task Held => _held.Task;

        public void HoldNext() => _hold = 1;

        public void Release() => _released.TrySetResult();

        public async Task UpsertAsync(ProjectManifest manifest, CancellationToken cancellationToken)
        {
            if (Interlocked.Exchange(ref _hold, 0) == 1)
            {
                _held.TrySetResult();
                await _released.Task;
            }

            Last = manifest;
        }

        public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task RemoveAsync(string recordingId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task SetTranscriptAsync(string recordingId, string text, IReadOnlyList<string> renamedSpeakers, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LibraryQueryResult> QueryAsync(LibraryQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<ProcessingEntry>> ListProcessingAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<string>> ListIdsByStateAsync(string state, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, int>> CountByFolderAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> RebuildAsync(CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
