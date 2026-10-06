using Memento.Audio.Capture;
using Memento.Audio.Recording;
using Memento.Audio.Writing;

namespace Memento.Audio.Tests.Recording;

/// <summary>The session façade end to end with real-time synthetic sources (no audio hardware).</summary>
public sealed class AudioRecordingSessionTests : IDisposable
{
    private const string Mic = "mic:{0.0.1.00000000}.{11111111-0000-4000-8000-000000000001}";
    private const string App = "app:4242";
    private readonly TempDirectory _dir = new();
    private readonly SyntheticCaptureFactory _factory = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task PauseIsExcludedFromEveryTrackAndTracksStayAligned()
    {
        await using var session = await Start(Mic, App);
        await Task.Delay(600);
        session.Pause();
        Assert.Equal(AudioSessionState.Paused, session.State);
        await Task.Delay(300);
        var gap = session.Resume();
        await Task.Delay(400);
        var result = await session.StopAsync();

        Assert.NotNull(gap);
        Assert.InRange(gap!.Duration.TotalMilliseconds, 280, 420);
        var sessionGap = Assert.Single(result.Gaps);
        Assert.Equal(gap, sessionGap);
        Assert.InRange(result.Duration.TotalMilliseconds, 950, 1_200);
        Assert.Equal(SessionStopReason.Requested, result.StopReason);
        Assert.Equal(2, result.Tracks.Count);
        Assert.Equal(["mic", "app-4242"], result.Tracks.Select(t => t.FileStem));
        foreach (var track in result.Tracks)
        {
            Assert.Equal(TrackEndReason.SessionStopped, track.EndReason);
            Assert.Equal(TimeSpan.Zero, track.StartOffset);
            Assert.Null(track.EndedEarlyAt);
            Assert.Equal(AudioFormat.Pcm24(48_000, 2), track.StorageFormat);
            Assert.InRange((track.Duration - result.Duration).Duration().TotalMilliseconds, 0, 15);
            var trackGap = Assert.Single(track.Gaps);
            Assert.Equal(gap.Duration, trackGap.Duration);
            Assert.InRange((trackGap.At - gap.At).Duration().TotalMilliseconds, 0, 1);
            var set = WavTrackSet.FromParts(track.Parts);
            Assert.Equal(track.Frames, set.TotalFrames);
            Assert.All(set.Parts, p => Assert.False(p.HeaderNeedsRepair));
        }

        Assert.InRange(Math.Abs(result.Tracks[0].Frames - result.Tracks[1].Frames), 0, 2);
        Assert.All(_factory.Opened, c => Assert.True(c.Disposed));
    }

    [Fact]
    public async Task LoopbackStylePacketsStampedAheadAreStillCutAtTheStopAndPauseInstants()
    {
        // Endpoint loopback stamps packets with presentation time, ~10–20 ms ahead of their arrival.
        _factory.TimestampLead[App] = TimeSpan.FromMilliseconds(15);
        await using var session = await Start(Mic, App);
        await Task.Delay(400);
        session.Pause();
        await Task.Delay(200);
        session.Resume();
        await Task.Delay(400);
        var result = await session.StopAsync();

        var mic = result.Tracks.Single(t => t.SourceId == Mic);
        var app = result.Tracks.Single(t => t.SourceId == App);
        Assert.InRange((app.Duration - result.Duration).Duration().TotalMilliseconds, 0, 1);
        Assert.InRange((mic.Duration - result.Duration).Duration().TotalMilliseconds, 0, 15);
        Assert.InRange((Assert.Single(mic.Gaps).At - Assert.Single(app.Gaps).At).Duration().TotalMilliseconds, 0, 0.05); // within a frame
    }

    [Fact]
    public async Task CheckpointsAreRaisedOnTheIntervalWithDrift()
    {
        var checkpoints = new List<SessionCheckpoint>();
        await using var session = await Start(new AudioRecordingOptions(_dir.Path, [Mic]) { CheckpointInterval = TimeSpan.FromMilliseconds(300) }, s => s.Checkpointed += (_, e) =>
        {
            lock (checkpoints)
            {
                checkpoints.Add(e.Checkpoint);
            }
        });
        await Task.Delay(1_450);
        await session.StopAsync();

        SessionCheckpoint last;
        lock (checkpoints)
        {
            Assert.InRange(checkpoints.Count, 3, 6);
            last = checkpoints[^1];
        }

        var track = Assert.Single(last.Tracks);
        Assert.Equal(Mic, track.SourceId);
        Assert.True(track.Frames > 0);
        Assert.NotNull(track.DriftPpm);
        Assert.InRange(track.DriftPpm!.Value, -100, 100);
        Assert.NotNull(session.LastCheckpointAt);
    }

    [Fact]
    public async Task ALostSourceEndsOnlyItsTrackWithTheExactTime()
    {
        _factory.LoseAfter[App] = TimeSpan.FromMilliseconds(400);
        SourceLostEventArgs? lost = null;
        using var raised = new SemaphoreSlim(0);
        await using var session = await Start(new AudioRecordingOptions(_dir.Path, [Mic, App]), s => s.SourceLost += (_, e) =>
        {
            lost = e;
            raised.Release();
        });

        Assert.True(await raised.WaitAsync(TimeSpan.FromSeconds(3)));
        await Task.Delay(400);
        var result = await session.StopAsync();

        Assert.Equal(App, lost!.SourceId);
        Assert.Equal(CaptureLostReason.DeviceInvalidated, lost.Reason);
        Assert.Equal([Mic], lost.Remaining);
        Assert.InRange(lost.At.TotalMilliseconds, 380, 520);
        Assert.Contains("was disconnected or disabled at 0:00", lost.Message, StringComparison.Ordinal);
        var app = result.Tracks.Single(t => t.SourceId == App);
        var mic = result.Tracks.Single(t => t.SourceId == Mic);
        Assert.Equal(TrackEndReason.SourceLost, app.EndReason);
        Assert.Equal(lost.At, app.EndedEarlyAt);
        Assert.InRange((app.EndedEarlyAt!.Value - app.Duration).TotalMilliseconds, 0, 30);
        Assert.Equal(TrackEndReason.SessionStopped, mic.EndReason);
        Assert.True(mic.Duration > app.Duration + TimeSpan.FromMilliseconds(300));
        Assert.Equal(SessionStopReason.Requested, result.StopReason);
    }

    [Fact]
    public async Task LosingTheLastSourceStopsTheSession()
    {
        _factory.LoseAfter[Mic] = TimeSpan.FromMilliseconds(250);
        await using var session = await Start(Mic);

        var result = await session.Completion.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(SessionStopReason.AllSourcesLost, result.StopReason);
        Assert.Contains("every source was lost", result.StopMessage, StringComparison.Ordinal);
        Assert.Equal(AudioSessionState.Stopped, session.State);
    }

    [Fact]
    public async Task SourcesCanBeAddedRemovedAndReAddedMidSession()
    {
        await using var session = await Start(Mic);
        await Task.Delay(300);
        var added = await session.SetSourceAsync(App, enabled: true, CancellationToken.None);
        Assert.True(added.IsActive);
        Assert.Equal("app-4242", added.FileStem);
        await Task.Delay(400);
        var removed = await session.SetSourceAsync(App, enabled: false, CancellationToken.None);
        Assert.Equal(TrackEndReason.Disabled, removed.EndReason);
        var again = await session.SetSourceAsync(App, enabled: true, CancellationToken.None);
        Assert.Equal("app-4242-2", again.FileStem);
        await Task.Delay(200);
        var result = await session.StopAsync();

        Assert.Equal(3, result.Tracks.Count);
        var first = result.Tracks[1];
        Assert.InRange(first.StartOffset.TotalMilliseconds, 300, 450);
        Assert.InRange(first.EndedEarlyAt!.Value.TotalMilliseconds, 690, 850);
        Assert.InRange((first.StartOffset + first.Duration - first.EndedEarlyAt.Value).Duration().TotalMilliseconds, 0, 15);
        Assert.Equal(TrackEndReason.SessionStopped, result.Tracks[2].EndReason);
        Assert.True(File.Exists(_dir.File("app-4242-2.wav")));
    }

    [Fact]
    public async Task PadLateTracksStartsThemAtTimelineZero()
    {
        await using var session = await Start(new AudioRecordingOptions(_dir.Path, [Mic]) { PadLateTracks = true });
        await Task.Delay(300);
        await session.SetSourceAsync(App, enabled: true, CancellationToken.None);
        await Task.Delay(300);
        var result = await session.StopAsync();

        var app = result.Tracks.Single(t => t.SourceId == App);
        Assert.Equal(TimeSpan.Zero, app.StartOffset);
        Assert.InRange((result.Tracks[0].Frames - app.Frames) / 48.0, -2, 2);
    }

    [Fact]
    public async Task ASourceThatCannotOpenStartsNothing()
    {
        _factory.Unavailable.Add(App);

        var ex = await Assert.ThrowsAsync<AudioSourceUnavailableException>(() => AudioRecordingSession.StartAsync(Options(Mic, App), CancellationToken.None));

        Assert.Equal(AudioSourceId.Parse(App), ex.SourceId);
        Assert.All(_factory.Opened, c => Assert.True(c.Disposed));
        Assert.Empty(Directory.GetFiles(_dir.Path));
    }

    [Fact]
    public async Task NoSourcesIsRejected()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => AudioRecordingSession.StartAsync(Options(), CancellationToken.None));
    }

    [Fact]
    public async Task LevelsArePublishedAtMostThirtyTimesPerSecond()
    {
        var events = new List<LevelsEventArgs>();
        await using var session = await Start(new AudioRecordingOptions(_dir.Path, [Mic, App]) { LevelInterval = TimeSpan.FromMilliseconds(5) }, s => s.Levels += (_, e) =>
        {
            lock (events)
            {
                events.Add(e);
            }
        });
        await Task.Delay(1_000);
        await session.StopAsync();

        lock (events)
        {
            Assert.InRange(events.Count, 20, 31);
            var later = events.Skip(5).ToList();
            Assert.All(later, e => Assert.Equal(2, e.Levels.Count));
            Assert.Contains(later, e => Math.Abs(e.Levels[0].Peak - 0.5f) < 0.01f && Math.Abs(e.Levels[0].Rms - 0.354f) < 0.02f);
        }
    }

    [Fact]
    public async Task OperationsAfterStopAreRejected()
    {
        var session = await Start(Mic);
        await Task.Delay(100);
        await session.StopAsync();

        Assert.Throws<InvalidOperationException>(session.Pause);
        await Assert.ThrowsAsync<InvalidOperationException>(() => session.SetSourceAsync(App, true, CancellationToken.None));
        var again = await session.StopAsync();
        Assert.Equal(SessionStopReason.Requested, again.StopReason);
        await session.DisposeAsync();
    }

    private AudioRecordingOptions Options(params string[] ids) => new(_dir.Path, ids) { CaptureFactory = _factory, Describe = _ => null, DurableCheckpoints = false };

    private Task<AudioRecordingSession> Start(params string[] ids) => AudioRecordingSession.StartAsync(Options(ids), CancellationToken.None);

    private async Task<AudioRecordingSession> Start(AudioRecordingOptions options, Action<AudioRecordingSession>? subscribe = null)
    {
        var session = await AudioRecordingSession.StartAsync(options with { CaptureFactory = _factory, Describe = _ => null, DurableCheckpoints = false }, CancellationToken.None);
        subscribe?.Invoke(session);
        return session;
    }
}
