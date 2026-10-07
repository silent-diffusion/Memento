namespace Memento.AI.Local;

/// <summary>
/// The per-model facts the VRAM budget and the pipeline need (the <c>llm</c> block of a catalog entry), measured in
/// the October 2026 spike: video memory ≈ <see cref="BaseVramBytes"/> + <see cref="KvBytesPerToken"/> × context.
/// </summary>
/// <param name="TemplateId">One of <see cref="LocalChatTemplates.All"/>.</param>
/// <param name="Architecture">The GGUF <c>general.architecture</c> the template was verified for.</param>
/// <param name="ContextTokens">Default context on a graphics card.</param>
/// <param name="MinContextTokens">The smallest context worth running on the graphics card; below it, run on the processor.</param>
/// <param name="CpuContextTokens">Context on the processor (prompt reading is slow, so smaller).</param>
/// <param name="Layers">Transformer blocks (all of them plus the output layer are offloaded for a full GPU load).</param>
/// <param name="BaseVramBytes">Weights, compute buffers and recurrent state with every layer offloaded.</param>
/// <param name="KvBytesPerToken">KV cache (f16) per context token.</param>
/// <param name="ChunkTokens">Transcript tokens per chunk at <see cref="ContextTokens"/>.</param>
public sealed record LocalModelProfile(
    string TemplateId,
    string Architecture,
    int ContextTokens,
    int MinContextTokens,
    int CpuContextTokens,
    int Layers,
    long BaseVramBytes,
    long KvBytesPerToken,
    int ChunkTokens)
{
    /// <summary>Estimated video memory for a full GPU load at <paramref name="contextTokens"/>.</summary>
    public long VramBytes(int contextTokens) => BaseVramBytes + (KvBytesPerToken * contextTokens);
}
