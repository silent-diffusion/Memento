using System.Diagnostics;
using Memento.Audio.Capture;
using Memento.Audio.Recording;
using Memento.Audio.Writing;

namespace Memento.Audio.Tests.Recording;

/// <summary>
/// The session façade end to end with real-time synthetic sources (no audio hardware). CI runners can stall for
/// hundreds of milliseconds, so no assertion relies on <c>Task.Delay</c> being punctual: times are checked against
/// <see cref="AudioRecordingSession.Elapsed"/> (or a stopwatch) read immediately before and after each action.
/// </summary>
public sealed class AudioRecordingSessionTests : IDisposable
{
    private const string Mic = "mic:{0.0.1.00000000}.{11111111-0000-4000-8000-000000000001}";
    private const string App = "app:4242";

    /// <summary>One synthetic packet (10 ms) plus scheduling slack inside the session.</summary>
    private const double PacketToleranceMs = 30;

    private readonly TempDirectory _dir = new();
    private readonly SyntheticCaptureFactory _factory = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task PauseIsExcludedFromEveryTrackAndTracksStayAligned()
    {
        await using var session = await Start(Mic, App);
        await Task.Delay(600);
        var beforePause = session.Elapsed;
        var outer = Stopwatch.StartNew();
        session.Pause();
        var inner = Stopwatch.StartNew();
        var afterPause = session.Elapsed;
        Assert.Equal(AudioSessionState.Paused, session.State);
        await Task.Delay(300);
        var pausedAtLeast = inner.Elapsed;
        var gap = session.Resume();
        var pausedAtMost = outer.Elapsed;
        await Task.Delay(400);
        var beforeStop = session.Elapsed;
        var result = await session.StopAsync();

        Assert.NotNull(gap);
        AssertBetween(gap!.Duration, pausedAtLeast, pausedAtMost);
        AssertBetween(gap.At, beforePause, afterPause);
        var sessionGap = Assert.Single(result.Gaps);
        Assert.Equal(gap, sessionGap);
        Assert.True(result.Duration >= beforeStop, $"The result ({result.Duration}) ends before Stop was called ({beforeStop})");
        Assert.True(result.Duration > gap.At, "Recording continued after the pause");
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

        // Drift needs a second of timestamps: wait for three checkpoints and at least 1.4 s of recording.
        var waited = Stopwatch.StartNew();
        while ((Count() < 3 || session.Elapsed < TimeSpan.FromMilliseconds(1_400)) && waited.Elapsed < Patience.Ceiling)
        {
            await Task.Delay(50);
        }

        var elapsed = session.Elapsed;
        await session.StopAsync();

        SessionCheckpoint last;
        lock (checkpoints)
        {
            // At least three, and never more often than the 300 ms interval allows (+1 for rounding, +1 at stop).
            Assert.InRange(checkpoints.Count, 3, (int)(elapsed.TotalMilliseconds / 300) + 2);
            last = checkpoints[^1];
        }

        var track = Assert.Single(last.Tracks);
        Assert.Equal(Mic, track.SourceId);
        Assert.True(track.Frames > 0);
        Assert.NotNull(track.DriftPpm);
        Assert.InRange(track.DriftPpm!.Value, -100, 100);
        Assert.NotNull(session.LastCheckpointAt);

        int Count()
        {
            lock (checkpoints)
            {
                return checkpoints.Count;
            }
        }
    }

    [Fact]
    public async Task ALostSourceEndsOnlyItsTrackWithTheExactTime()
    {
        _factory.LoseAfter[App] = TimeSpan.FromMilliseconds(400);
        SourceLostEventArgs? lost = null;
        var raisedAt = TimeSpan.Zero;
        using var raised = new SemaphoreSlim(0);
        AudioRecordingSession? started = null;
        await using var session = await Start(new AudioRecordingOptions(_dir.Path, [Mic, App]), s =>
        {
            started = s;
            s.SourceLost += (_, e) =>
            {
                raisedAt = started.Elapsed;
                lost = e;
                raised.Release();
            };
        });

        Assert.True(await raised.WaitAsync(Patience.Ceiling));
        var afterLoss = session.Elapsed;
        await Task.Delay(400);
        var result = await session.StopAsync();

        Assert.Equal(App, lost!.SourceId);
        Assert.Equal(CaptureLostReason.DeviceInvalidated, lost.Reason);
        Assert.Equal([Mic], lost.Remaining);

        // The synthetic device goes away exactly 400 ms after its own start.
        var capture = _factory.Opened.Single(c => c.Source.ToString() == App);
        var wentAway = TimeSpan.FromTicks(capture.StartedAtQpc + TimeSpan.FromMilliseconds(400).Ticks - session.StartedAtQpc);
        Assert.InRange((lost.At - wentAway).Duration().TotalMilliseconds, 0, 1);
        Assert.True(lost.At <= raisedAt, $"Lost at {lost.At}, after the event was raised at {raisedAt}");
        Assert.Contains("was disconnected or disabled at ", lost.Message, StringComparison.Ordinal);
        var app = result.Tracks.Single(t => t.SourceId == App);
        var mic = result.Tracks.Single(t => t.SourceId == Mic);
        Assert.Equal(TrackEndReason.SourceLost, app.EndReason);
        Assert.Equal(lost.At, app.EndedEarlyAt);
        Assert.InRange((app.EndedEarlyAt!.Value - app.Duration).Duration().TotalMilliseconds, 0, PacketToleranceMs);
        Assert.Equal(TrackEndReason.SessionStopped, mic.EndReason);
        Assert.True(mic.Duration >= afterLoss, "The microphone kept recording after the other source was lost");
        Assert.True(mic.Duration > app.Duration);
        Assert.Equal(SessionStopReason.Requested, result.StopReason);
    }

    [Fact]
    public async Task LosingTheLastSourceStopsTheSession()
    {
        _factory.LoseAfter[Mic] = TimeSpan.FromMilliseconds(250);
        await using var session = await Start(Mic);

        var result = await session.Completion.WaitAsync(Patience.Ceiling);

        Assert.Equal(SessionStopReason.AllSourcesLost, result.StopReason);
        Assert.Contains("every source was lost", result.StopMessage, StringComparison.Ordinal);
        Assert.Equal(AudioSessionState.Stopped, session.State);
        var track = Assert.Single(result.Tracks);
        Assert.Equal(TrackEndReason.SourceLost, track.EndReason);
        Assert.True(track.Duration >= TimeSpan.FromMilliseconds(250 - PacketToleranceMs), $"Stopped at {track.Duration}, before the device went away");
    }

    [Fact]
    public async Task SourcesCanBeAddedRemovedAndReAddedMidSession()
    {
        await using var session = await Start(Mic);
        await Task.Delay(300);
        var beforeAdd = session.Elapsed;
        var added = await session.SetSourceAsync(App, enabled: true, CancellationToken.None);
        var afterAdd = session.Elapsed;
        Assert.True(added.IsActive);
        Assert.Equal("app-4242", added.FileStem);
        await Task.Delay(400);
        var beforeRemove = session.Elapsed;
        var removed = await session.SetSourceAsync(App, enabled: false, CancellationToken.None);
        var afterRemove = session.Elapsed;
        Assert.Equal(TrackEndReason.Disabled, removed.EndReason);
        var beforeReAdd = session.Elapsed;
        var again = await session.SetSourceAsync(App, enabled: true, CancellationToken.None);
        var afterReAdd = session.Elapsed;
        Assert.Equal("app-4242-2", again.FileStem);
        await Task.Delay(200);
        var result = await session.StopAsync();

        Assert.Equal(["mic", "app-4242", "app-4242-2"], result.Tracks.Select(t => t.FileStem));
        var first = result.Tracks[1];
        var second = result.Tracks[2];
        // A track starts at its first packet: no earlier than the call, no later than the call or that packet.
        AssertBetween(first.StartOffset, beforeAdd, Later(afterAdd, FirstPacketAt(session, _factory.Opened[1])));
        AssertBetween(first.EndedEarlyAt!.Value, beforeRemove, afterRemove);
        Assert.InRange((first.StartOffset + first.Duration - first.EndedEarlyAt.Value).Duration().TotalMilliseconds, 0, 15);
        Assert.Equal(TrackEndReason.Disabled, first.EndReason);
        AssertBetween(second.StartOffset, beforeReAdd, Later(afterReAdd, FirstPacketAt(session, _factory.Opened[2])));
        Assert.True(second.StartOffset >= first.EndedEarlyAt.Value, "The source came back only after it was turned off");
        Assert.Equal(TrackEndReason.SessionStopped, second.EndReason);
        Assert.Equal(TrackEndReason.SessionStopped, result.Tracks[0].EndReason);
        Assert.Equal(TimeSpan.Zero, result.Tracks[0].StartOffset);
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

        // Timing-independent: however late the source was added, it is padded to start with the session.
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
        var waited = Stopwatch.StartNew();
        while (Count() < 10 && waited.Elapsed < Patience.Ceiling)
        {
            await Task.Delay(50);
        }

        await session.StopAsync();
        var elapsed = session.Elapsed;

        lock (events)
        {
            // Never more than 30 per second of recording (+1 for the first), however late the delay returned.
            Assert.InRange(events.Count, 10, (int)Math.Ceiling(elapsed.TotalSeconds * 30) + 1);
            var later = events.Skip(5).ToList();
            Assert.All(later, e => Assert.Equal(2, e.Levels.Count));
            Assert.Contains(later, e => Math.Abs(e.Levels[0].Peak - 0.5f) < 0.01f && Math.Abs(e.Levels[0].Rms - 0.354f) < 0.02f);
        }

        int Count()
        {
            lock (events)
            {
                return events.Count;
            }
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

    /// <summary><paramref name="actual"/> lies between two readings taken around the action, within a packet.</summary>
    private static void AssertBetween(TimeSpan actual, TimeSpan before, TimeSpan after) =>
        Assert.InRange(actual.TotalMilliseconds, before.TotalMilliseconds - PacketToleranceMs, after.TotalMilliseconds + PacketToleranceMs);

    private static TimeSpan Later(TimeSpan a, TimeSpan b) => a > b ? a : b;

    /// <summary>Timeline time of a synthetic capture's first packet (its start plus its start-up latency).</summary>
    private static TimeSpan FirstPacketAt(AudioRecordingSession session, SyntheticCapture capture) =>
        TimeSpan.FromTicks(capture.StartedAtQpc + SyntheticCapture.DefaultLatency.Ticks - session.StartedAtQpc);

    private AudioRecordingOptions Options(params string[] ids) => new(_dir.Path, ids) { CaptureFactory = _factory, Describe = _ => null, DurableCheckpoints = false };

    private Task<AudioRecordingSession> Start(params string[] ids) => AudioRecordingSession.StartAsync(Options(ids), CancellationToken.None);

    private async Task<AudioRecordingSession> Start(AudioRecordingOptions options, Action<AudioRecordingSession>? subscribe = null)
    {
        var session = await AudioRecordingSession.StartAsync(options with { CaptureFactory = _factory, Describe = _ => null, DurableCheckpoints = false }, CancellationToken.None);
        subscribe?.Invoke(session);
        return session;
    }
}
