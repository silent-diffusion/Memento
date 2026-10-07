namespace Memento.Core.Workers;

/// <summary>Whisper.net runtimes a transcription job may name (<see cref="TranscribeJob.Runtimes"/>).</summary>
public static class WorkerRuntimes
{
    public const string Vulkan = "vulkan";
    public const string Cpu = "cpu";

    /// <summary>
    /// The name of the machine-wide lock a worker holds while it uses the graphics card, so two workers (another
    /// Memento, a tool, a worker left over from a host that was killed) never share it.
    /// </summary>
    public const string GpuLockName = @"Local\Memento.Worker.Gpu";

    /// <summary>Overrides <see cref="GpuLockName"/> for a worker started by the tests.</summary>
    public const string GpuLockVariable = "MEMENTO_WORKER_GPU_LOCK";
}
