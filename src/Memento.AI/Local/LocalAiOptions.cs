namespace Memento.AI.Local;

/// <summary>Settings for <see cref="LocalAiProvider"/>.</summary>
public sealed record LocalAiOptions
{
    /// <summary>One of <see cref="LocalLlmDevices"/>.</summary>
    public string Device { get; init; } = LocalLlmDevices.Auto;

    /// <summary>The context to ask for; 0 for the model's default.</summary>
    public int ContextTokens { get; init; }

    public long VramMarginBytes { get; init; } = 256L * 1024 * 1024;

    public long SpillThresholdBytes { get; init; } = 384L * 1024 * 1024;

    public int Threads { get; init; }

    /// <summary>A larger model's placement from <see cref="MemoryPlanner"/>; <c>null</c> for the small models.</summary>
    public LocalLlmPlacement? Placement { get; init; }

    /// <summary>llama.cpp's flash attention (<c>null</c>: its own choice).</summary>
    public bool? FlashAttention { get; init; }
}
