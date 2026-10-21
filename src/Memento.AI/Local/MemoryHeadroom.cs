namespace Memento.AI.Local;

/// <summary>
/// What <see cref="MemoryPlanner"/> keeps free for the rest of the PC and the app (ARCHITECTURE.md §8, ENGINE-NOTES.md §T
/// for the measurements behind each number).
/// </summary>
public sealed record MemoryHeadroom
{
    private const long Mib = 1024L * 1024;

    /// <summary>
    /// RAM kept available for Windows and the other programs beyond everything planned. Held down to 4, 2 and 1 GB
    /// available, the 35B with its experts in RAM still wrote at 10.6–12.3 tokens/s and a small program still started in
    /// 25 ms, but prompt reading slowed from 103 to 76–86 tokens/s and the CPU build's repacked load left 0.2 GB; and
    /// what other programs take can change in the middle of a 20-minute document (ENGINE-NOTES.md §T.6).
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

    /// <summary>
    /// A split puts at least this share of the blocks on the card (and always at least one). Any split is worth it: with
    /// the output layer and 9 of 64 blocks on the card Qwen3.5 27B read prompts at 52 tokens/s against 4.5 in RAM alone,
    /// because llama.cpp hands the prompt's large matrix work to the card (ENGINE-NOTES.md §T.4).
    /// </summary>
    public double MinCardShare { get; init; }

    /// <summary>Prompt reading with a dense model's blocks in RAM: about this many bytes of those blocks a second (27B, 48–54 blocks in RAM: 36–52 tokens/s).</summary>
    public double BlockCopyBytesPerSecond { get; init; } = 0.9e9;

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

    /// <summary>Prompt reading in RAM alone is compute-bound: about this many weight bytes a second (35B-A3B: 27 tokens/s at 2.6 GB a token; 27B: 4.5 at 15.6 GB).</summary>
    public double RamPromptBytesPerSecond { get; init; } = 65e9;

    /// <summary>Prompt reading with a mixture of experts' experts in RAM: llama.cpp copies them to the card for each 512-token batch at about this speed (35B-A3B: 100 tokens/s).</summary>
    public double CopyBytesPerSecond { get; init; } = 3.5e9;

    /// <summary>Prompt reading with every layer on the card (Qwen3.5 4B: 1,731 tokens/s at 2.6 GB of weights).</summary>
    public double CardPromptBytesPerSecond { get; init; } = 4.5e12;

    /// <summary>What Meeting minutes of the 20-minute synthetic meeting reads (ENGINE-NOTES.md §T.5), for the plan's time estimate.</summary>
    public int MeetingPromptTokens { get; init; } = 57_000;

    /// <summary>What it writes; see <see cref="MeetingPromptTokens"/>.</summary>
    public int MeetingOutputTokens { get; init; } = 5_000;

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
