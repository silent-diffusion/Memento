using Memento.Core.Projects;
using Memento.Core.Settings;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Transcripts;

public sealed class TranscriptStoreTests : IDisposable
{
    private readonly TempDirectory _directory = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.Zero));
    private readonly ProjectStore _projects;
    private readonly TranscriptStore _store;
    private readonly HistorySettings _history = new();
    private string _id = string.Empty;

    public TranscriptStoreTests()
    {
        _projects = new ProjectStore(new FakeLibraryLocation(_directory.Path), _time, NullLogger<ProjectStore>.Instance);
        _store = new TranscriptStore(_projects, _time, NullLogger<TranscriptStore>.Instance);
    }

    public void Dispose() => _directory.Dispose();

    private async Task<string> ProjectAsync()
    {
        var manifest = await _projects.CreateAsync(new ProjectCreateRequest("T", "meeting", _time.GetLocalNow(), ProjectStates.Ready), CancellationToken.None);
        _id = manifest.Id;
        return _id;
    }

    private Task<TranscriptDocument?> WriteAsync(string reason, Func<TranscriptDocument?, TranscriptDocument?> update, HistorySettings? history = null) =>
        _store.UpdateAsync(_id, reason, history ?? _history, update, CancellationToken.None);

    [Fact]
    public async Task EveryWriteIncrementsTheVersionAndIsAtomic()
    {
        await ProjectAsync();

        var first = await WriteAsync(TranscriptChangeReasons.Transcribed, _ => TranscriptFixtures.Meeting());
        var second = await WriteAsync(TranscriptChangeReasons.Edited, t => t! with { Reviewed = true });

        Assert.Equal(1, first!.Version);
        Assert.Equal(2, second!.Version);
        var loaded = await _store.LoadAsync(_id, CancellationToken.None);
        Assert.Equal(2, loaded!.Version);
        Assert.True(loaded.Reviewed);
        Assert.Equal(TranscriptChangeReasons.Edited, loaded.LastChange!.Reason);
        Assert.False(File.Exists(Path.Combine(_projects.GetProjectFolder(_id), "transcript.json.tmp")));
    }

    [Fact]
    public async Task TheReplacedTranscriptIsKeptAsAVersionWithItsOwnReason()
    {
        await ProjectAsync();
        await WriteAsync(TranscriptChangeReasons.Transcribed, _ => TranscriptFixtures.Meeting());
        _time.Advance(TimeSpan.FromMinutes(1));

        await WriteAsync(TranscriptChangeReasons.Edited, t => t! with { Reviewed = true });
        _time.Advance(TimeSpan.FromMinutes(1));
        await WriteAsync(TranscriptChangeReasons.Retranscribed, _ => TranscriptFixtures.Document(TranscriptFixtures.Segment("s0001", 0, 1, "New.")));

        var versions = await _store.ListVersionsAsync(_id, CancellationToken.None);
        Assert.Equal([TranscriptChangeReasons.Edited, TranscriptChangeReasons.Transcribed], versions.Select(v => v.Reason));
        Assert.Equal(3, versions[1].Transcript.Segments.Count);
        Assert.True(versions[0].Transcript.Reviewed);
        var loaded = await _store.LoadVersionAsync(_id, versions[1].Id, CancellationToken.None);
        Assert.Equal(1, loaded!.Transcript.Version);
    }

    [Fact]
    public async Task ARunOfEditsKeepsOneVersion()
    {
        await ProjectAsync();
        await WriteAsync(TranscriptChangeReasons.Transcribed, _ => TranscriptFixtures.Meeting());
        await WriteAsync(TranscriptChangeReasons.Edited, t => t! with { Reviewed = true });
        _time.Advance(TimeSpan.FromMinutes(2));
        await WriteAsync(TranscriptChangeReasons.Edited, t => t! with { Reviewed = false });
        _time.Advance(TimeSpan.FromMinutes(2));
        await WriteAsync(TranscriptChangeReasons.Edited, t => t! with { Reviewed = true });

        Assert.Single(await _store.ListVersionsAsync(_id, CancellationToken.None));

        // However long the run, it stays one version; a restore starts a new run.
        _time.Advance(TimeSpan.FromHours(5));
        await WriteAsync(TranscriptChangeReasons.Edited, t => t! with { Reviewed = false });
        Assert.Single(await _store.ListVersionsAsync(_id, CancellationToken.None));

        _time.Advance(TimeSpan.FromMinutes(1));
        await WriteAsync(TranscriptChangeReasons.Restored, t => t);
        _time.Advance(TimeSpan.FromMinutes(1));
        await WriteAsync(TranscriptChangeReasons.Edited, t => t! with { Reviewed = true });
        Assert.Equal(
            [TranscriptChangeReasons.Restored, TranscriptChangeReasons.Edited, TranscriptChangeReasons.Transcribed],
            (await _store.ListVersionsAsync(_id, CancellationToken.None)).Select(v => v.Reason));
    }

    [Fact]
    public async Task APassReplacingAPartialTranscriptKeepsNoVersion()
    {
        await ProjectAsync();
        await WriteAsync(TranscriptChangeReasons.Transcribed, _ => TranscriptFixtures.Meeting() with { Complete = false });

        await WriteAsync(TranscriptChangeReasons.Transcribed, _ => TranscriptFixtures.Meeting());

        Assert.Empty(await _store.ListVersionsAsync(_id, CancellationToken.None));
    }

    [Fact]
    public async Task ASpeakersPassIsNotAVersionAndKeepsTheContentReason()
    {
        await ProjectAsync();
        await WriteAsync(TranscriptChangeReasons.Transcribed, _ => TranscriptFixtures.Meeting());

        var saved = await WriteAsync(TranscriptChangeReasons.Speakers, t => t);

        Assert.Empty(await _store.ListVersionsAsync(_id, CancellationToken.None));
        Assert.Equal(TranscriptChangeReasons.Transcribed, saved!.LastChange!.Reason);
    }

    [Fact]
    public async Task NoVersionsAreKeptWhenHistoryIsOff()
    {
        await ProjectAsync();
        var off = new HistorySettings { KeepVersions = false };
        await WriteAsync(TranscriptChangeReasons.Transcribed, _ => TranscriptFixtures.Meeting(), off);
        await WriteAsync(TranscriptChangeReasons.Retranscribed, _ => TranscriptFixtures.Meeting(), off);

        Assert.Empty(await _store.ListVersionsAsync(_id, CancellationToken.None));
    }

    [Fact]
    public async Task VersionsOlderThanKeepDaysArePruned()
    {
        await ProjectAsync();
        var history = new HistorySettings { KeepDays = 7 };
        await WriteAsync(TranscriptChangeReasons.Transcribed, _ => TranscriptFixtures.Meeting(), history);
        await WriteAsync(TranscriptChangeReasons.Retranscribed, _ => TranscriptFixtures.Meeting(), history);
        Assert.Single(await _store.ListVersionsAsync(_id, CancellationToken.None));

        _time.Advance(TimeSpan.FromDays(8));
        await WriteAsync(TranscriptChangeReasons.Retranscribed, _ => TranscriptFixtures.Meeting(), history);

        var versions = await _store.ListVersionsAsync(_id, CancellationToken.None);
        Assert.Equal(TranscriptChangeReasons.Retranscribed, Assert.Single(versions).Reason);
    }

    [Fact]
    public async Task AnUnknownVersionIdReadsAsNull()
    {
        await ProjectAsync();

        Assert.Null(await _store.LoadVersionAsync(_id, "../../project", CancellationToken.None));
        Assert.Null(await _store.LoadVersionAsync(_id, "20261006T100000000Z", CancellationToken.None));
    }

    [Fact]
    public async Task ThePartialFileRoundTripsAndIsDeleted()
    {
        await ProjectAsync();
        var partial = new TranscriptPartial(1, "small|en|GPU|words", [new TranscriptPartialTrack("mic", 2, 5, false, 3000, [[1.0, 2.0]])], TranscriptFixtures.Meeting().Segments, "en", 1234);

        await _store.SavePartialAsync(_id, partial, CancellationToken.None);
        var loaded = await _store.LoadPartialAsync(_id, CancellationToken.None);

        Assert.Equal("small|en|GPU|words", loaded!.Signature);
        Assert.Equal(2, loaded.Tracks[0].WindowsDone);
        Assert.Equal(15, loaded.LastSegmentEnd);
        _store.DeletePartial(_id);
        Assert.Null(await _store.LoadPartialAsync(_id, CancellationToken.None));
    }
}
