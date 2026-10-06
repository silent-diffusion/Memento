using System.Text.Json;
using Memento.Core.Audio;
using Memento.Core.Projects;
using Memento.Core.Tests.Audio;
using Memento.Core.Tests.Fakes;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.Recovery;

public sealed class RecoveryServiceTests : IDisposable
{
    private static readonly DateTimeOffset Start = new(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(1));

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    /// <summary>Records through a first "process", then kills it mid-write: no stop, no finalize, buffers lost.</summary>
    private async Task<string> RecordAndCrashAsync()
    {
        var first = new BridgeTestHost(directory: _directory);
        await first.ResultAsync("settings.set", """{"recording":{"checkpointSeconds":5}}""");
        var (_, recordingId) = await first.StartAsync("Interrupted sync", Mic, SystemAudio);
        first.Session.Advance(TimeSpan.FromSeconds(6));
        await first.Session.DrainAsync();
        await WaitUntilAsync(
            async () => (await first.Store.ReadRecordingStateAsync(recordingId, CancellationToken.None))?.LastCheckpointAt is not null,
            "the first checkpoint");
        first.Session.Advance(TimeSpan.FromSeconds(3));
        await first.Session.SimulateCrashAsync(flushBufferedSamples: false);
        first.DisposeServicesOnly();
        return recordingId;
    }

    [Fact]
    public async Task AnAbandonedRecordingIsRepairedFinalizedAndOffered()
    {
        var recordingId = await RecordAndCrashAsync();
        using var second = new BridgeTestHost(directory: _directory);
        var folder = second.Store.GetProjectFolder(recordingId);

        // The crash left headers covering only the first checkpoint.
        Assert.True(WavInfo.Read(Path.Combine(folder, "tracks", "mic.wav")).DurationMs < 7000);

        var recovered = await second.Recovery.RunAsync(CancellationToken.None);

        Assert.Equal([recordingId], recovered);
        var manifest = await second.Store.LoadAsync(recordingId, CancellationToken.None);
        Assert.Equal(ProjectStates.Recovered, manifest.State);
        var recovery = manifest.Recovery!;
        Assert.Equal(2, recovery.TracksIntact);
        Assert.Equal(2, recovery.TracksTotal);
        Assert.InRange(recovery.RecoveredDurationMs, 5000, 9000);
        Assert.Equal(recovery.RecoveredDurationMs, manifest.DurationMs);
        Assert.InRange(recovery.MayBeMissingMs, 1, 5000);
        Assert.NotNull(recovery.LastCheckpointAt);
        Assert.False(recovery.Acknowledged);

        // Repaired and finalized like any recording; nothing was deleted.
        var micInfo = WavInfo.Read(Path.Combine(folder, "tracks", "mic.wav"));
        Assert.True(micInfo.DurationMs >= 6000);
        Assert.All(manifest.Tracks, t => Assert.NotNull(t.Sha256));
        Assert.Equal(await FileHashes.Sha256Async(Path.Combine(folder, "tracks", "mic.wav"), CancellationToken.None), manifest.Tracks[0].Sha256);
        Assert.True(File.Exists(Path.Combine(folder, "mix.wav")));
        Assert.True(File.Exists(Path.Combine(folder, "peaks.json")));
        Assert.True(File.Exists(Path.Combine(folder, "tracks", "system.wav")));
        Assert.False(second.Store.HasRecordingState(recordingId));
        var history = await second.Store.ReadHistoryAsync(recordingId, CancellationToken.None);
        Assert.Contains(history, h => h.Stage == "recovered" && h.Summary == "Recovered after Memento closed during recording");
        Assert.Contains(history, h => h.Stage == "stored" && h.Event == "completed");

        // The dialog: listed until acknowledged; acknowledging changes nothing else.
        var list = await second.ResultAsync("recovery.list");
        var item = Assert.Single(list.GetProperty("items").EnumerateArray());
        Assert.Equal(recordingId, item.GetProperty("recordingId").GetString());
        Assert.Equal("Interrupted sync", item.GetProperty("title").GetString());
        Assert.Equal(2, item.GetProperty("tracksIntact").GetInt32());
        Assert.Equal(recovery.MayBeMissingMs, item.GetProperty("mayBeMissingMs").GetInt64());

        await second.ResultAsync("recovery.acknowledge", JsonSerializer.Serialize(new { recordingId }));

        Assert.Empty((await second.ResultAsync("recovery.list")).GetProperty("items").EnumerateArray());
        var row = Assert.Single((await second.ResultAsync("library.list")).GetProperty("recordings").EnumerateArray());
        Assert.Equal("recovered", row.GetProperty("state").GetString());
        Assert.Empty(await second.Recovery.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task AFinalizeInterruptedAfterStopLosesNothing()
    {
        using var host = new BridgeTestHost(directory: _directory);
        var manifest = await host.Store.CreateAsync(new ProjectCreateRequest("Stopped", "meeting", Start, ProjectStates.Finalizing), CancellationToken.None);
        var folder = host.Store.GetProjectFolder(manifest.Id);
        var format = PcmFormat.Pcm16(48_000, 1);
        WavTestFiles.Write(Path.Combine(folder, "tracks", "mic.wav"), format, 96_000, (f, _) => MathF.Sin(f / 10f) * 0.2f);
        await host.Store.WriteRecordingStateAsync(
            new RecordingStateDocument
            {
                SessionId = "s",
                RecordingId = manifest.Id,
                StartedAt = Start,
                State = "stopped",
                CheckpointSeconds = 30,
                Tracks = [new RecordingStateTrack("mic", Mic, "microphone", "Mic", "tracks/mic.wav", 48_000, 1, 16, "pcm", 0, 192_000, null, null)],
            },
            CancellationToken.None);

        await host.Recovery.RunAsync(CancellationToken.None);

        var recovered = await host.Store.LoadAsync(manifest.Id, CancellationToken.None);
        Assert.Equal(ProjectStates.Recovered, recovered.State);
        Assert.Equal(2000, recovered.Recovery!.RecoveredDurationMs);
        Assert.Equal(0, recovered.Recovery.MayBeMissingMs);
        Assert.Equal(2000, recovered.Mix!.DurationMs);
    }

    [Fact]
    public async Task AMissingTrackIsCountedButNeverStopsRecovery()
    {
        using var host = new BridgeTestHost(directory: _directory);
        var manifest = await host.Store.CreateAsync(new ProjectCreateRequest("Half", "meeting", Start, ProjectStates.Recording), CancellationToken.None);
        var folder = host.Store.GetProjectFolder(manifest.Id);
        WavTestFiles.Write(Path.Combine(folder, "tracks", "mic.wav"), PcmFormat.Pcm16(48_000, 1), 48_000, (_, _) => 0.1f);
        await host.Store.WriteRecordingStateAsync(
            new RecordingStateDocument
            {
                SessionId = "s",
                RecordingId = manifest.Id,
                StartedAt = Start,
                LastCheckpointAt = Start.AddSeconds(1),
                CheckpointSeconds = 30,
                Tracks =
                [
                    new RecordingStateTrack("mic", Mic, "microphone", "Mic", "tracks/mic.wav", 48_000, 1, 16, "pcm", 0, 96_000, null, null),
                    new RecordingStateTrack("system", SystemAudio, "system", "System", "tracks/system.wav", 48_000, 2, 16, "pcm", 0, 0, null, null),
                ],
            },
            CancellationToken.None);

        await host.Recovery.RunAsync(CancellationToken.None);

        var recovered = await host.Store.LoadAsync(manifest.Id, CancellationToken.None);
        Assert.Equal(ProjectStates.Recovered, recovered.State);
        Assert.Equal(1, recovered.Recovery!.TracksIntact);
        Assert.Equal(2, recovered.Recovery.TracksTotal);
        Assert.Equal(1000, recovered.Recovery.RecoveredDurationMs);
        Assert.Equal(30_000, recovered.Recovery.MayBeMissingMs);
        Assert.Equal(1000, recovered.Mix!.DurationMs);
        Assert.Contains(await host.Store.ReadHistoryAsync(manifest.Id, CancellationToken.None), h => h.Detail?.Contains("no audio file", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task APreviouslyFailedFinalizeIsRetriedQuietly()
    {
        using var host = new BridgeTestHost(directory: _directory);
        var manifest = await host.Store.CreateAsync(new ProjectCreateRequest("Retry", "meeting", Start, ProjectStates.Failed), CancellationToken.None);
        var folder = host.Store.GetProjectFolder(manifest.Id);
        WavTestFiles.Write(Path.Combine(folder, "tracks", "mic.wav"), PcmFormat.Pcm16(48_000, 1), 4800, (_, _) => 0.1f);
        await host.Store.SaveAsync(
            (await host.Store.LoadAsync(manifest.Id, CancellationToken.None)) with
            {
                Tracks = [new ProjectTrack { Id = "mic", SourceId = Mic, SourceKind = "microphone", Name = "Mic", File = "tracks/mic.wav", CaptureFile = "tracks/mic.wav", SampleRate = 48_000, Channels = 1, BitsPerSample = 16 }],
            },
            CancellationToken.None);
        await host.Store.WriteRecordingStateAsync(new RecordingStateDocument { SessionId = "s", RecordingId = manifest.Id, State = "stopped" }, CancellationToken.None);

        var recovered = await host.Recovery.RunAsync(CancellationToken.None);

        Assert.Empty(recovered);
        Assert.Equal(ProjectStates.Ready, (await host.Store.LoadAsync(manifest.Id, CancellationToken.None)).State);
        Assert.Empty((await host.ResultAsync("recovery.list")).GetProperty("items").EnumerateArray());
    }
}
