namespace Memento.AI.Local;

/// <summary>
/// A local model job, as plain data so it can cross the worker's JSON-lines protocol: load the model once (within a
/// video memory budget), then run <see cref="Prompts"/> in order (or count <see cref="TokenizeTexts"/>), then unload.
/// </summary>
public sealed record LocalLlmJob
{
    /// <summary>The installed GGUF file.</summary>
    public required string ModelPath { get; init; }

    /// <summary>Catalog id (<c>qwen3-5-4b-q4</c>).</summary>
    public required string ModelId { get; init; }

    /// <summary>The name the interface uses ("Qwen3.5 4B"), for error copy.</summary>
    public required string ModelName { get; init; }

    public required LocalModelProfile Profile { get; init; }

    /// <summary>One of <see cref="LocalLlmDevices"/>.</summary>
    public string Device { get; init; } = LocalLlmDevices.Auto;

    /// <summary>The context to ask for; 0 for the profile default (smaller when video memory is short).</summary>
    public int ContextTokens { get; init; }

    /// <summary>Layers to offload, overriding the budget (tests and diagnostics); <c>null</c> to plan from free video memory.</summary>
    public int? GpuLayers { get; init; }

    /// <summary>Free video memory the host measured (DXGI), used when the engine cannot read it itself.</summary>
    public long? FreeVramBytes { get; init; }

    /// <summary>Kept free on the graphics card beyond the estimate.</summary>
    public long VramMarginBytes { get; init; } = 256L * 1024 * 1024;

    /// <summary>
    /// Growth of this process's shared GPU memory, beyond what it had before loading, that counts as a spill out of
    /// the card's own memory (Windows then runs 10-25x slower; normal runs stay under 100 MB).
    /// </summary>
    public long SpillThresholdBytes { get; init; } = 384L * 1024 * 1024;

    /// <summary>Processor threads; 0 for the physical core count.</summary>
    public int Threads { get; init; }

    /// <summary>Run one short generation after loading (the first decode is ~8x slower while shaders compile).</summary>
    public bool WarmUp { get; init; } = true;

    public IReadOnlyList<LocalLlmPrompt> Prompts { get; init; } = [];

    /// <summary>Instead of generating: count the tokens of each text with the model's tokenizer.</summary>
    public IReadOnlyList<string>? TokenizeTexts { get; init; }
}
