using Memento.Core.Workers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Transcription.Tests;

/// <summary>The real Memento.Worker.exe with real models on this PC's GPU and CPU.</summary>
[Trait("Category", "Hardware")]
public sealed class WorkerHardwareTests
{
    private static WorkerClient Client() =>
        new(new ProcessWorkerLauncher(new WorkerLocation(WorkerBuild.Executable!), NullLogger<ProcessWorkerLauncher>.Instance), NullLogger<WorkerClient>.Instance);

    private static TranscribeJob Job(IReadOnlyList<string> runtimes) =>
        new(
            [new WorkerTrack("mic", WorkerBuild.SpeechWav!, 0)],
            Path.Combine(WorkerBuild.ModelsRoot, "whisper", "ggml-small.bin"),
            "whisper-small",
            runtimes,
            -1,
            null,
            "auto",
            TranscriptionDefaults.Prompt,
            TranscriptionDefaults.CpuThreads,
            true,
            600,
            5);

    [HardwareFact(@"whisper\ggml-small.bin")]
    public async Task SmallTranscribesSpeechWithWordsOnVulkanOrCpu()
    {
        var segments = new List<WorkerSegment>();
        WorkerDevice? device = null;

        var result = await Client().RunAsync(
            new WorkerJob(WorkerJobKinds.Transcribe, Job([TranscriptionDefaults.RuntimeVulkan, TranscriptionDefaults.RuntimeCpu])),
            reply =>
            {
                segments.AddRange(reply.Segments ?? []);
                device = reply.Device ?? device;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.NotEmpty(segments);
        Assert.NotNull(device);
        Assert.All(segments, s => Assert.InRange(s.Confidence, 0, 1));
        Assert.Contains(segments, s => s.Words.Count > 0);
        Assert.True(result.Transcription!.AudioSeconds > 0);
    }

    [HardwareFact(@"sherpa-onnx\pyannote-segmentation-3-0.onnx", @"sherpa-onnx\nemo_en_titanet_small.onnx")]
    public async Task SpeakersAreFoundOnTheCpu()
    {
        var result = await Client().RunAsync(
            new WorkerJob(
                WorkerJobKinds.Diarize,
                Diarize: new DiarizeJob(
                    [new WorkerTrack("mic", WorkerBuild.SpeechWav!, 0)],
                    Path.Combine(WorkerBuild.ModelsRoot, "sherpa-onnx", "pyannote-segmentation-3-0.onnx"),
                    Path.Combine(WorkerBuild.ModelsRoot, "sherpa-onnx", "nemo_en_titanet_small.onnx"),
                    -1,
                    TranscriptionDefaults.ClusteringThreshold,
                    TranscriptionDefaults.DiarizationThreads)),
            null,
            CancellationToken.None);

        var turns = Assert.Single(result.Diarization!.Tracks).Turns;
        Assert.NotEmpty(turns);
        Assert.All(turns, t => Assert.True(t.End > t.Start));
    }

    [HardwareFact(@"whisper\ggml-small.bin")]
    public async Task KillingTheWorkerIsReportedAsACrash()
    {
        using var cancel = new CancellationTokenSource();
        var client = Client();
        var run = client.RunAsync(
            new WorkerJob(WorkerJobKinds.Transcribe, Job([TranscriptionDefaults.RuntimeCpu])),
            reply =>
            {
                if (reply.Type == WorkerMessageTypes.Track)
                {
                    foreach (var process in System.Diagnostics.Process.GetProcessesByName("Memento.Worker"))
                    {
                        process.Kill();
                    }
                }

                return Task.CompletedTask;
            },
            cancel.Token);

        await Assert.ThrowsAsync<WorkerCrashedException>(() => run);
    }
}
