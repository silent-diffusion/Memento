using System.Diagnostics;
using Memento.Audio.Adapters;
using Memento.Audio.Capture;
using Memento.Audio.Tests.Recording;
using Memento.Audio.Writing;
using Memento.Core.Audio;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;
using Microsoft.Extensions.Logging.Abstractions;
using A = Memento.Audio.Recording;

namespace Memento.Audio.Tests.Adapters;

/// <summary>
/// The Core adapter over a real <see cref="A.AudioRecordingSession"/> fed by synthetic real-time sources (the seam
/// is the capture factory, so no audio hardware is involved). Timing is checked against readings taken around each
/// action, never against <c>Task.Delay</c> being punctual.
/// </summary>
public sealed class WasapiRecordingSessionTests : IDisposable
{
    private const string MicId = "mic:{0.0.1.00000000}.{11111111-0000-4000-8000-000000000001}";
    private const string AppId = "app:4242";
    private const double ToleranceMs = 30;

    private static readonly AudioSource Mic = new(MicId, "microphone", "Synthetic microphone", "Test", true, null);
    private static readonly AudioSource App = new(AppId, "application", "Synthetic app", "Only this app", false, 4242);

    private readonly TempDirectory _dir = new();
    private readonly SyntheticCaptureFactory _factory = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public async Task ASessionMapsTracksPausesAndEventsAndStopsOnce()
    {
        var states = new List<(RecordingSessionState State, bool OnPool)>();
        var checkpoints = new List<CheckpointEventArgs>();
        var levels = new List<SourceLevel>();
        await using var session = await StartAsync(TimeSpan.FromMilliseconds(300), Mic, App);
        session.StateChanged += (_, e) => Add(states, (e.State, Thread.CurrentThread.IsThreadPoolThread));
        session.CheckpointWritten += (_, e) => Add(checkpoints, e);
        session.LevelsAvailable += (_, e) =>
        {
            lock (levels)
            {
                levels.AddRange(e.Levels);
            }
        };

        Assert.Equal(RecordingSessionState.Recording, session.State);
        Assert.StartsWith("wasapi-", session.SessionId, StringComparison.Ordinal);
        await Task.Delay(500);
        var outer = Stopwatch.StartNew();
        await session.PauseAsync(CancellationToken.None);
        var inner = Stopwatch.StartNew();
        Assert.Equal(RecordingSessionState.Paused, session.State);
        await Task.Delay(200);
        var pausedAtLeast = inner.Elapsed;
        await session.ResumeAsync(CancellationToken.None);
        var pausedAtMost = outer.Elapsed;
        var mark = await session.MarkAsync(CancellationToken.None);
        Assert.InRange(mark, session.ElapsedMs - 50, session.ElapsedMs);
        await WaitUntil(() => Count(checkpoints) >= 1, "a checkpoint");
        var tracks = await session.SetSourceAsync(App, enabled: false, CancellationToken.None);
        Assert.Equal(Core.Recording.TrackEndReason.Disabled, tracks.Single(t => t.Source.Id == AppId).EndReason);
        await Task.Delay(200);

        var result = await session.StopAsync(CancellationToken.None);
        var again = await session.StopAsync(CancellationToken.None);

        Assert.Same(result, again);
        Assert.Equal(RecordingSessionState.Stopped, session.State);
        Assert.Null(result.StoppedBy);
        Assert.Equal(["mic", "app-4242"], result.Tracks.Select(t => t.TrackId));
        Assert.Equal(["tracks/mic.wav", "tracks/app-4242.wav"], result.Tracks.Select(t => t.File));
        Assert.All(result.Tracks, t => Assert.Equal(new PcmFormat(48_000, 2, 24, SampleEncoding.Pcm), t.Format));
        var mic = result.Tracks[0];
        var app = result.Tracks[1];
        Assert.Same(Mic, mic.Source);
        Assert.Null(mic.EndReason);
        Assert.Null(mic.EndedAtMs);
        Assert.Equal(Core.Recording.TrackEndReason.Disabled, app.EndReason);
        Assert.NotNull(app.EndedAtMs);
        Assert.True(app.EndedAtMs < mic.DurationMs);
        Assert.Equal(WavTrackSet.Open(_dir.File("tracks"), "mic").TotalDataBytes, mic.DataBytes);
        Assert.Equal(mic.DataBytes, mic.CheckpointedBytes);
        Assert.InRange(mic.DurationMs - result.ElapsedMs, -15, 15);

        var pause = Assert.Single(result.Pauses);
        Assert.InRange(pause.DurationMs!.Value, pausedAtLeast.TotalMilliseconds - ToleranceMs, pausedAtMost.TotalMilliseconds + ToleranceMs);
        Assert.InRange(pause.AtMs, 400, mark);

        await WaitUntil(() => Count(states) >= 3, "the stopped state");
        lock (states)
        {
            Assert.Equal([RecordingSessionState.Paused, RecordingSessionState.Recording, RecordingSessionState.Stopped], states.Select(s => s.State));
            Assert.All(states, s => Assert.True(s.OnPool, "events are raised on the dispatcher, not on the caller's or an audio thread"));
        }

        lock (checkpoints)
        {
            var first = checkpoints[0];
            Assert.Equal(2, first.Tracks.Count);
            Assert.All(first.Tracks, t => Assert.True(t.CheckpointedBytes > 0));
        }

        lock (levels)
        {
            Assert.Contains(levels, l => l.SourceId == MicId && Math.Abs(l.Peak - 0.5) < 0.01);
        }
    }

    [Fact]
    public async Task AMidSessionSourceGetsItsOffsetBeforeItsFirstPacket()
    {
        await using var session = await StartAsync(TimeSpan.FromSeconds(30), Mic);
        await Task.Delay(300);
        var before = session.ElapsedMs;

        var tracks = await session.SetSourceAsync(App, enabled: true, CancellationToken.None);
        var after = session.ElapsedMs;

        var app = tracks.Single(t => t.Source.Id == AppId);
        Assert.Equal("app-4242", app.TrackId);
        Assert.Same(App, app.Source);
        Assert.InRange(app.StartOffsetMs, before, after + 60);
        Assert.True(app.IsOpen);
        await Task.Delay(200);
        var result = await session.StopAsync(CancellationToken.None);
        Assert.True(result.Tracks.Single(t => t.TrackId == "app-4242").StartOffsetMs >= before);
    }

    [Fact]
    public async Task ASourceThatCannotOpenIsNamedInCoresException()
    {
        _factory.Unavailable.Add(AppId);

        var ex = await Assert.ThrowsAsync<SourceUnavailableException>(() => StartAsync(TimeSpan.FromSeconds(30), Mic, App));

        Assert.Equal(AppId, ex.SourceId);
        Assert.Equal("Synthetic app", ex.SourceName);
        Assert.Equal("Windows could not open it", ex.Reason);
        Assert.Empty(Directory.GetFiles(_dir.File("tracks")));

        await using var session = await StartAsync(TimeSpan.FromSeconds(30), Mic);
        var midSession = await Assert.ThrowsAsync<SourceUnavailableException>(() => session.SetSourceAsync(App, enabled: true, CancellationToken.None));
        Assert.Equal("Synthetic app", midSession.SourceName);
        Assert.Single(session.Tracks);
    }

    [Fact]
    public async Task LosingEverySourceStopsTheSessionWithDeviceLost()
    {
        _factory.LoseAfter[MicId] = TimeSpan.FromMilliseconds(250);
        var lost = new TaskCompletionSource<SourceLostEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource<HostStoppedEventArgs>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var session = await StartAsync(TimeSpan.FromSeconds(30), Mic);
        session.SourceLost += (_, e) => lost.TrySetResult(e);
        session.HostStopped += (_, e) => stopped.TrySetResult(e);

        var host = await stopped.Task.WaitAsync(Patience.Ceiling);
        var lostSource = await lost.Task.WaitAsync(Patience.Ceiling);
        var result = await session.StopAsync(CancellationToken.None);

        Assert.Equal(HostStopReason.DeviceLost, host.Reason);
        Assert.Same(host.Result, result);
        Assert.Equal(HostStopReason.DeviceLost, result.StoppedBy);
        Assert.Equal("mic", lostSource.Track.TrackId);
        Assert.Empty(lostSource.Remaining);
        Assert.True(lostSource.AtMs >= 250 - ToleranceMs);
        Assert.Equal(Core.Recording.TrackEndReason.SourceLost, Assert.Single(result.Tracks).EndReason);
        Assert.Contains("every source was lost", host.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(A.SessionStopReason.Requested, null)]
    [InlineData(A.SessionStopReason.DiskFull, HostStopReason.DiskFull)]
    [InlineData(A.SessionStopReason.AllSourcesLost, HostStopReason.DeviceLost)]
    [InlineData(A.SessionStopReason.WriteFailed, HostStopReason.Error)]
    public void StopReasonsMapToCore(A.SessionStopReason reason, HostStopReason? expected) =>
        Assert.Equal(expected, WasapiRecordingSession.MapStopReason(reason));

    [Theory]
    [InlineData(A.TrackEndReason.SessionStopped, false, null)]
    [InlineData(A.TrackEndReason.SourceLost, false, Core.Recording.TrackEndReason.SourceLost)]
    [InlineData(A.TrackEndReason.Disabled, false, Core.Recording.TrackEndReason.Disabled)]
    [InlineData(A.TrackEndReason.WriteFailed, true, Core.Recording.TrackEndReason.DiskFull)]
    [InlineData(A.TrackEndReason.WriteFailed, false, Core.Recording.TrackEndReason.Error)]
    public void TrackEndReasonsMapToCore(A.TrackEndReason reason, bool diskFull, Core.Recording.TrackEndReason? expected) =>
        Assert.Equal(expected, WasapiRecordingSession.MapEndReason(reason, diskFull));

    [Theory]
    [InlineData(unchecked((int)0x80070005), "Could not open the microphone for recording. Windows denied access. Allow …", "Windows is blocking microphone access for desktop apps; allow it in Settings › Privacy & security › Microphone")]
    [InlineData(0, "The app (process 42) is no longer running. Start it again or choose another source.", "the app is no longer running")]
    [InlineData(0, "The microphone is no longer connected (it may have been unplugged). Reconnect it.", "it is disconnected or disabled")]
    [InlineData(0, "The output device is disabled or unplugged. Reconnect or enable it.", "it is disconnected or disabled")]
    [InlineData(0, "Per-app capture needs Windows 10 version 2004 or later. Record everything this PC plays instead.", "per-app capture needs Windows 10 version 2004 or later")]
    [InlineData(unchecked((int)0x88890008), "Could not open the microphone for recording. Windows reported 0x88890008.", "Windows reported 0x88890008")]
    public void UnavailableReasonsAreShortPhrases(int hresult, string message, string expected) =>
        Assert.Equal(expected, SourceUnavailableReason.From(new AudioSourceUnavailableException(AudioSourceId.Parse(MicId), message, hresult)));

    [Fact]
    public void CoreFindsTheSamePartFilesTheWriterCreates()
    {
        for (var i = 1; i <= 5; i++)
        {
            Assert.Equal("tracks/" + WavTrackSet.PartFileName("mic", i), CaptureParts.PartPath("tracks/mic.wav", i));
        }
    }

    [Fact]
    public void SourcesAreShapedLikeTheBridgeContract()
    {
        var app = WasapiAudioSourceProvider.ToContract(new Memento.Audio.Sources.AudioSourceInfo(AppId, AudioSourceKind.Application, "Zoom", "Only this app", false, 4242));
        var mic = WasapiAudioSourceProvider.ToContract(new Memento.Audio.Sources.AudioSourceInfo(MicId, AudioSourceKind.Microphone, "Microphone Array", "Built-in", true, null));
        var system = WasapiAudioSourceProvider.ToContract(new Memento.Audio.Sources.AudioSourceInfo("system:{0.0.0.00000000}.{x}", AudioSourceKind.System, "Everything this PC plays", "Default output, Speakers", true, null));

        Assert.Equal(new AudioSource(AppId, "application", "Zoom", "Only this app", false, 4242), app);
        Assert.Equal(new AudioSource(MicId, "microphone", "Microphone Array", "Built-in", true, null), mic);
        Assert.Equal("system", system.Kind);
        Assert.Null(system.ProcessId);
    }

    private static void Add<T>(List<T> list, T item)
    {
        lock (list)
        {
            list.Add(item);
        }
    }

    private static int Count<T>(List<T> list)
    {
        lock (list)
        {
            return list.Count;
        }
    }

    private static async Task WaitUntil(Func<bool> condition, string what)
    {
        var waited = Stopwatch.StartNew();
        while (!condition())
        {
            if (waited.Elapsed > Patience.Ceiling)
            {
                throw new TimeoutException($"Timed out waiting for {what}.");
            }

            await Task.Delay(20);
        }
    }

    private async Task<IRecordingSession> StartAsync(TimeSpan checkpointInterval, params AudioSource[] sources)
    {
        var engine = new WasapiRecordingEngine(
            new WasapiEngineOptions { CaptureFactory = _factory, Describe = _ => null, DurableCheckpoints = false },
            TimeProvider.System,
            NullLoggerFactory.Instance);
        Assert.Equal("WASAPI", engine.Name);
        return await engine.StartAsync(new RecordingPlan(_dir.Path, sources, checkpointInterval), CancellationToken.None);
    }
}
