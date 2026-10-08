using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Recording.Simulation;
using Memento.Core.Tests.Fakes;
using Xunit.Abstractions;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.Soak;

/// <summary>
/// Long real-time recording on the simulated engine: three tracks, 10 s checkpoints, bounded memory.
/// Length from <c>MEMENTO_SOAK_MINUTES</c> (30 by default), a sample every <c>MEMENTO_SOAK_SAMPLE_SECONDS</c>
/// (a sixth of the length, at most 5 minutes, by default); <c>MEMENTO_SOAK_CSV</c> names a file that receives one row
/// per sample (managed heap, working set, private bytes, handles, threads).
/// </summary>
public sealed class RecordingSoakTests(ITestOutputHelper output)
{
    [SoakFact]
    [Trait("Category", "Soak")]
    public async Task ThreeTracksForThirtyMinutesStayBounded()
    {
        var minutes = double.Parse(Environment.GetEnvironmentVariable("MEMENTO_SOAK_MINUTES") ?? "30", CultureInfo.InvariantCulture);
        using var host = new BridgeTestHost(new SimulatedEngineOptions { Speed = 1 });
        await host.ResultAsync("settings.set", """{"recording":{"checkpointSeconds":10}}""");
        var (sessionId, recordingId) = await host.StartAsync("Soak", Mic, SystemAudio, App);
        var process = Process.GetCurrentProcess();
        var sampleEvery = Environment.GetEnvironmentVariable("MEMENTO_SOAK_SAMPLE_SECONDS") is { } every
            ? TimeSpan.FromSeconds(double.Parse(every, CultureInfo.InvariantCulture))
            : TimeSpan.FromMinutes(Math.Min(5, minutes / 6));
        var samples = new List<(TimeSpan At, long Managed, long WorkingSet, long Private, int Handles, int Threads)>();
        var clock = Stopwatch.StartNew();
        var csv = Environment.GetEnvironmentVariable("MEMENTO_SOAK_CSV");
        if (!string.IsNullOrWhiteSpace(csv))
        {
            await File.WriteAllTextAsync(csv, "elapsed_s,managed_mb,working_set_mb,private_mb,handles,threads,recorded_s\n");
        }

        void Sample()
        {
            process.Refresh();
            samples.Add((clock.Elapsed, GC.GetTotalMemory(forceFullCollection: true), process.WorkingSet64, process.PrivateMemorySize64, process.HandleCount, process.Threads.Count));
            var last = samples[^1];
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{last.At:hh\\:mm\\:ss} managed {last.Managed / 1048576.0:0.0} MB · working set {last.WorkingSet / 1048576.0:0.0} MB · private {last.Private / 1048576.0:0.0} MB · handles {last.Handles} · threads {last.Threads} · events {host.Sink.Posted.Count}"));
            if (!string.IsNullOrWhiteSpace(csv))
            {
                File.AppendAllText(csv, string.Create(CultureInfo.InvariantCulture, $"{last.At.TotalSeconds:0},{last.Managed / 1048576.0:0.0},{last.WorkingSet / 1048576.0:0.0},{last.Private / 1048576.0:0.0},{last.Handles},{last.Threads},{host.Session.ElapsedMs / 1000.0:0.0}\n"));
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(15));
        Sample();
        while (clock.Elapsed < TimeSpan.FromMinutes(minutes))
        {
            await Task.Delay(sampleEvery);
            host.Sink.Clear(); // the test sink keeps every event; the real sink forwards and forgets
            Sample();
        }

        var elapsedBeforeStop = host.Session.ElapsedMs;
        var overruns = host.Session.Overruns;
        await host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));
        var finalizeClock = Stopwatch.StartNew();
        await host.Recordings.WhenIdleAsync();
        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"finalize took {finalizeClock.Elapsed.TotalSeconds:0.0} s"));
        Sample();

        var manifest = await host.Store.LoadAsync(recordingId, CancellationToken.None);
        var folder = host.Store.GetProjectFolder(recordingId);
        foreach (var file in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{Path.GetRelativePath(folder, file),-24} {new FileInfo(file).Length / 1048576.0,10:0.0} MB"));
        }

        output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"recorded {manifest.DurationMs / 1000.0:0.0} s (wall {elapsedBeforeStop / 1000.0:0.0} s at stop) · {overruns} packets dropped · state {manifest.State}"));
        var levels = host.Sink.Payloads(BridgeEventNames.RecordingLevels).Count;
        output.WriteLine($"levels events since last sample: {levels}");

        Assert.Equal("ready", manifest.State);
        Assert.Equal(0, overruns);
        Assert.All(manifest.Tracks, t => Assert.InRange(t.DurationMs, manifest.DurationMs - 50, manifest.DurationMs));
        var steady = samples.Skip(1).Take(samples.Count - 2).ToList();
        if (steady.Count >= 2)
        {
            // Bounded: the managed heap does not grow with recording length.
            Assert.True(steady[^1].Managed - steady[0].Managed < 32L * 1024 * 1024, "Managed memory grew by more than 32 MB while recording.");
            Assert.True(steady[^1].Private - steady[0].Private < 64L * 1024 * 1024, "Private bytes grew by more than 64 MB while recording.");
            Assert.True(steady[^1].Handles - steady[0].Handles < 100, $"Handles grew from {steady[0].Handles} to {steady[^1].Handles} while recording.");
        }
    }
}
