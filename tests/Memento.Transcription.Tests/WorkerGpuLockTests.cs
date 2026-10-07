using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Memento.Core.Workers;

namespace Memento.Transcription.Tests;

/// <summary>
/// The real Memento.Worker.exe takes the machine-wide GPU lock before a job that may use the graphics card, and waits
/// while another process holds it (another worker, another Memento, a tool). The test holds a lock of its own name.
/// </summary>
public sealed class WorkerGpuLockTests
{
    private static TranscribeJob Job(string[] runtimes) => new(
        [new WorkerTrack("mic", Path.Combine(Path.GetTempPath(), "no-such-track.flac"), 0)],
        Path.Combine(Path.GetTempPath(), "no-such-model.bin"),
        "whisper-small",
        runtimes,
        -1,
        null,
        "auto",
        "Hello.",
        1,
        true,
        600,
        5);

    private static Process Start(string exe, string lockName, TranscribeJob job)
    {
        var info = new ProcessStartInfo(exe)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
        };
        info.Environment[WorkerRuntimes.GpuLockVariable] = lockName;
        var process = Process.Start(info)!;
        process.BeginErrorReadLine();
        process.StandardInput.WriteLine(JsonSerializer.Serialize(new WorkerCommand(WorkerMessageTypes.Start, new WorkerJob(WorkerJobKinds.Transcribe, job)), WorkerJsonContext.Default.WorkerCommand));
        process.StandardInput.Flush();
        return process;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Process, System.Collections.Concurrent.BlockingCollection<string>> Lines = new();

    /// <summary>The next protocol line within <paramref name="timeout"/>, or <c>null</c>.</summary>
    private static WorkerReply? Next(Process process, TimeSpan timeout)
    {
        var lines = Lines.GetValue(process, p =>
        {
            var queue = new System.Collections.Concurrent.BlockingCollection<string>();
            p.OutputDataReceived += (_, e) =>
            {
                if (e.Data is { } line && line.StartsWith("{\"type\":", StringComparison.Ordinal))
                {
                    queue.Add(line);
                }
            };
            p.BeginOutputReadLine();
            return queue;
        });
        return lines.TryTake(out var next, timeout) ? JsonSerializer.Deserialize(next, WorkerJsonContext.Default.WorkerReply) : null;
    }

    [Fact]
    public void AGraphicsCardJobWaitsWhileAnotherProcessHoldsTheGpuLock()
    {
        if (WorkerBuild.Executable is not { } exe)
        {
            return; // Not built in this run.
        }

        var name = $@"Local\Memento.Worker.Gpu.Test.{Guid.NewGuid():N}";
        using var held = new Mutex(initiallyOwned: true, name);
        using var worker = Start(exe, name, Job(["vulkan", "cpu"]));
        try
        {
            Assert.Equal(WorkerMessageTypes.Ready, Next(worker, TimeSpan.FromSeconds(30))?.Type);
            var waiting = Next(worker, TimeSpan.FromSeconds(10));
            Assert.Equal((WorkerMessageTypes.Log, "Waiting for another worker to finish with the graphics card."), (waiting?.Type, waiting?.Message));

            // Nothing else happens while the lock is held.
            Assert.Null(Next(worker, TimeSpan.FromSeconds(1.5)));
            Assert.False(worker.HasExited);

            held.ReleaseMutex();
            var error = Next(worker, TimeSpan.FromSeconds(10));
            Assert.Equal((WorkerMessageTypes.Error, WorkerErrorCodes.ModelLoad), (error?.Type, error?.Code));
            Assert.True(worker.WaitForExit(10_000));
        }
        finally
        {
            if (!worker.HasExited)
            {
                worker.Kill();
            }
        }
    }

    [Fact]
    public void AProcessorJobDoesNotWaitForTheGpuLock()
    {
        if (WorkerBuild.Executable is not { } exe)
        {
            return;
        }

        var name = $@"Local\Memento.Worker.Gpu.Test.{Guid.NewGuid():N}";
        using var held = new Mutex(initiallyOwned: true, name);
        using var worker = Start(exe, name, Job(["cpu"]));
        try
        {
            Assert.Equal(WorkerMessageTypes.Ready, Next(worker, TimeSpan.FromSeconds(30))?.Type);
            var error = Next(worker, TimeSpan.FromSeconds(10));
            Assert.Equal((WorkerMessageTypes.Error, WorkerErrorCodes.ModelLoad), (error?.Type, error?.Code));
        }
        finally
        {
            if (!worker.HasExited)
            {
                worker.Kill();
            }

            held.ReleaseMutex();
        }
    }
}
