namespace Memento.AI.Local;

/// <summary>Samples this process's dedicated and shared GPU memory (the spill watch's input).</summary>
public interface IGpuProcessMemory
{
    /// <summary><c>null</c> when the counters cannot be read.</summary>
    GpuProcessMemorySample? Sample();
}
