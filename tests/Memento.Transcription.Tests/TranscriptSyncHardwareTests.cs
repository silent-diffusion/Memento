using System.Globalization;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Transcription.Tests;

/// <summary>
/// Line times against the audio (ENGINE-NOTES.md §K): the synthetic two-voice recording has 30 lines after silences
/// of 1.5–20 s, at known times up to 3:37. Before the fix Whisper started every line in the pause before it (up to
/// 3.2 s early) and, after the 20 s pause at 0:58, repeated one sentence until the end, so nothing after minute one
/// was transcribed.
/// </summary>
[Trait("Category", "Hardware")]
public sealed class TranscriptSyncHardwareTests
{
    [SyncHardwareFact]
    public async Task LineTimesMatchTheAudioWithin100MillisecondsPastMinuteThree()
    {
        var client = new WorkerClient(new ProcessWorkerLauncher(new WorkerLocation(WorkerBuild.Executable!), NullLogger<ProcessWorkerLauncher>.Instance), NullLogger<WorkerClient>.Instance);
        var job = new TranscribeJob(
            [new WorkerTrack("imported", SyncFixture.Wav!, 0)],
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
        var segments = new List<WorkerSegment>();

        await client.RunAsync(
            new WorkerJob(WorkerJobKinds.Transcribe, job),
            reply =>
            {
                segments.AddRange(reply.Segments ?? []);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        var lines = SyncFixture.Lines();
        Assert.Contains(lines, l => l.Start > 180);
        var early = new List<string>();
        var late = new List<string>();
        foreach (var (text, start, end) in lines)
        {
            // The line's first segment: the one starting closest to the voice, among those that start before it ends.
            var first = segments.Where(s => s.Start < end).MinBy(s => Math.Abs(s.Start - start));
            var error = first is null ? double.NaN : first.Start - start;
            var limit = start >= 120 ? 0.1 : 0.5;
            if (first is null || Math.Abs(error) > limit || !SharesWords(first.Text, text))
            {
                (start >= 120 ? late : early).Add(string.Create(CultureInfo.InvariantCulture, $"\"{text}\" at {start:0.00} s: {(first is null ? "no line" : $"\"{first.Text}\" at {first.Start:0.00} s ({error:+0.00;-0.00} s)")}"));
            }
        }

        // From minute two on (minute three included) every line starts within 100 ms of its voice. Before that within
        // 0.5 s, except at most one line whose first word the engine left out (large-v3-turbo drops a lone "Great.").
        Assert.True(late.Count == 0, $"{late.Count} lines after 2:00 missing or off:\n{string.Join('\n', late)}");
        Assert.True(early.Count <= 1, $"{early.Count} lines before 2:00 missing or off:\n{string.Join('\n', early)}");
    }

    private static bool SharesWords(string transcribed, string truth)
    {
        static HashSet<string> Words(string s) => s.ToLowerInvariant().Split([' ', ',', '.', '?'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var said = Words(truth);
        return Words(transcribed).Count(said.Contains) >= 2;
    }
}
