namespace Memento.AI.Local;

/// <summary>
/// What <see cref="MemoryPlanner"/> keeps free for the rest of the PC and the app (ARCHITECTURE.md §8, ENGINE-NOTES.md §T
/// for the measurements behind each number).
/// </summary>
public sealed record MemoryHeadroom
{
    private const long Mib = 1024L * 1024;

    /// <summary>
    /// RAM Windows keeps available beyond everything planned: below about 2 GB available the memory manager trims working
    /// sets and a model mapped from disk starts reading its pages again (the spike: 2.0 GB left, writing at half speed
    /// and the desktop stalling on window switches).
    /// </summary>
    public long OsBytes { get; init; } = 2048 * Mib;

    /// <summary>The app's own growth while the model writes: the WebView2 window, the Builder's live output and a document render.</summary>
    public long AppBytes { get; init; } = 512 * Mib;

    /// <summary>An audio recording's buffers, the meters and the live transcript's window (measured well under this).</summary>
    public long CaptureBytes { get; init; } = 512 * Mib;

    /// <summary>A recording with video: the encoder, its frame pool and the preview, on top of <see cref="CaptureBytes"/>.</summary>
    public long CaptureVideoBytes { get; init; } = 1024 * Mib;

    /// <summary>Kept free on the card beyond a split or expert placement (a 16k KV cache once moved 0.5 GB to shared memory with 5.3 GB reported free, ENGINE-NOTES.md §I.2).</summary>
    public long VramMarginBytes { get; init; } = 512 * Mib;

    /// <summary>The processor's compute buffers when blocks run in RAM (llama.cpp's CPU compute buffer at a 512-token micro-batch).</summary>
    public long CpuComputeBytes { get; init; } = 512 * Mib;

    /// <summary>A split puts at least this share of the blocks on the card; with fewer the graphics card's gate is held for little gain, so the model runs in RAM.</summary>
    public double MinCardShare { get; init; } = 0.15;

    /// <summary>
    /// Effective read speed of RAM for the weights on the processor, measured on the reference laptop (DDR4-3200, two
    /// channels): Qwen3.5 35B-A3B in RAM reads 2.6 GB a token at 6.1 tokens/s.
    /// </summary>
    public double RamBytesPerSecond { get; init; } = 16e9;

    /// <summary>Effective read speed of the card's memory for its layers (RTX 3060 Laptop, Vulkan: Qwen3.5 4B at 57 tokens/s).</summary>
    public double CardBytesPerSecond { get; init; } = 150e9;

    /// <summary>
    /// The cost of each hand-over between the card and the processor in one token's graph (copies and a synchronisation):
    /// a mixture of experts with its experts in RAM has two per block, a split by blocks about four.
    /// </summary>
    public double HandOverSeconds { get; init; } = 0.00045;

    public static MemoryHeadroom Default { get; } = new();

    /// <summary>The RAM kept for everything else now.</summary>
    public long RamReserve(Core.Engines.MemoryDemands demands)
    {
        ArgumentNullException.ThrowIfNull(demands);
        return OsBytes + AppBytes
            + (demands.Recording ? CaptureBytes + (demands.RecordingVideo ? CaptureVideoBytes : 0) : 0)
            + (demands.TranscriptionPending ? demands.TranscriptionRamBytes : 0);
    }
}
