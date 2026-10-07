namespace Memento.Core.Engines;

/// <summary>Where a transcription pass would run, as the engine selector decided it.</summary>
/// <param name="UseGpu">Vulkan on <see cref="Gpu"/>; otherwise the processor.</param>
/// <param name="Reason">Why the processor was chosen when there is a GPU ("not enough free video memory"), for History.</param>
public sealed record EngineDevice(bool UseGpu, GpuInfo? Gpu, string? Reason)
{
    /// <summary><c>GPU</c> or <c>CPU</c> (bridge wording).</summary>
    public string Kind => UseGpu ? "GPU" : "CPU";

    /// <summary>"local GPU" / "CPU" for progress labels.</summary>
    public string ProgressWord => UseGpu ? "local GPU" : "CPU";
}
