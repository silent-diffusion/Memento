using System.Globalization;
using System.Text.Json;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Transcription.Tests;

/// <summary>
/// A quiet microphone beside loud system audio (ENGINE-NOTES.md §Q), with the real worker and model: the two tracks of
/// <c>tools/e2e/levels-fixture.ps1</c> (<c>MEMENTO_LEVELS_DIR</c>: system.wav at a -12 dBFS peak, mic.wav at -38 dBFS over
/// -60 dBFS room noise, each with its truth) in one transcription job. Before the fix the worker called the microphone
/// track silent and skipped it (0 % coverage); now both tracks must be covered alike. Model: <c>MEMENTO_SYNC_MODEL</c>
/// (default large-v3-turbo). Run with <c>dotnet test --filter Category=Hardware</c>.
/// </summary>
[Trait("Category", "Hardware")]
public sealed class LevelsHardwareTests
{
    private static string? Folder => Environment.GetEnvironmentVariable("MEMENTO_LEVELS_DIR") is { Length: > 0 } dir && File.Exists(Path.Combine(dir, "mic.wav.json")) ? dir : null;

    [LevelsHardwareFact]
    public async Task TheQuietMicrophoneIsCoveredLikeTheLoudSystemAudio()
    {
        var folder = Folder!;
        var client = new WorkerClient(new ProcessWorkerLauncher(new WorkerLocation(WorkerBuild.Executable!), NullLogger<ProcessWorkerLauncher>.Instance), NullLogger<WorkerClient>.Instance);
        var job = new TranscribeJob(
            [new WorkerTrack("system", Path.Combine(folder, "system.wav"), 0), new WorkerTrack("mic", Path.Combine(folder, "mic.wav"), 0)],
            SyncFixture.ModelPath,
            "whisper",
            [TranscriptionDefaults.RuntimeVulkan, TranscriptionDefaults.RuntimeCpu],
            -1,
            null,
            "auto",
            TranscriptionDefaults.Prompt,
            TranscriptionDefaults.CpuThreads,
            true,
            600,
            5);
        var segments = new List<(string Track, WorkerSegment Segment)>();
        var silent = new List<string>();

        await client.RunAsync(
            new WorkerJob(WorkerJobKinds.Transcribe, job),
            reply =>
            {
                if (reply.Track is { Silent: true } track)
                {
                    silent.Add(track.TrackId);
                }

                segments.AddRange((reply.Segments ?? []).Select(s => (reply.TrackId ?? string.Empty, s)));
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Empty(silent);
        var system = Coverage(Path.Combine(folder, "system.wav.json"), segments.Where(s => s.Track == "system").Select(s => s.Segment).ToList());
        var mic = Coverage(Path.Combine(folder, "mic.wav.json"), segments.Where(s => s.Track == "mic").Select(s => s.Segment).ToList());

        Assert.True(system >= 0.95, string.Create(CultureInfo.InvariantCulture, $"system audio coverage {system:P1}"));
        Assert.True(mic >= 0.95, string.Create(CultureInfo.InvariantCulture, $"microphone coverage {mic:P1} (system audio {system:P1})"));
    }

    /// <summary>The share of the truth's speech with a word of the track within 1 s (ENGINE-NOTES.md §K).</summary>
    private static double Coverage(string truthFile, IReadOnlyList<WorkerSegment> segments)
    {
        using var truth = JsonDocument.Parse(File.ReadAllText(truthFile));
        var words = segments.SelectMany(s => s.Words.Count > 0 ? s.Words.Select(w => (w.S, w.E)) : [(s.Start, s.End)]).ToList();
        double total = 0, covered = 0;
        foreach (var line in truth.RootElement.GetProperty("lines").EnumerateArray())
        {
            for (var t = line.GetProperty("start").GetDouble(); t < line.GetProperty("end").GetDouble(); t += 0.05)
            {
                total += 0.05;
                covered += words.Any(w => t >= w.S - 1 && t <= w.E + 1) ? 0.05 : 0;
            }
        }

        return covered / total;
    }

    [AttributeUsage(AttributeTargets.Method)]
    private sealed class LevelsHardwareFactAttribute : FactAttribute
    {
        public LevelsHardwareFactAttribute()
        {
            if (WorkerBuild.Executable is null)
            {
                Skip = "Memento.Worker has not been built.";
            }
            else if (Folder is null)
            {
                Skip = "Set MEMENTO_LEVELS_DIR to a folder made by tools/e2e/levels-fixture.ps1 to run the level check.";
            }
            else if (!File.Exists(SyncFixture.ModelPath))
            {
                Skip = $"Model {SyncFixture.ModelPath} is not installed.";
            }
        }
    }
}
