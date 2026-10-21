namespace Memento.AI.Local;

/// <summary>
/// The per-model facts the VRAM budget and the pipeline need (the <c>llm</c> block of a catalog entry), measured in
/// the October 2026 spike: video memory ≈ <see cref="BaseVramBytes"/> + <see cref="KvBytesPerToken"/> × context.
/// The larger models (ENGINE-NOTES.md §T) add where their weights are, so <see cref="MemoryPlanner"/> can split them
/// between the graphics card and RAM; the small models leave those at zero and are planned as before.
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
/// <param name="LayerBytes">Larger models: the weights of one transformer block that are not routed experts (llama.cpp's buffer sizes, measured).</param>
/// <param name="ExpertBytes">Mixture of experts: the routed experts of all blocks together (the <c>ffn_*_exps</c> tensors); 0 for a dense model.</param>
/// <param name="OutputBytes">Larger models: the output layer, offloaded as the last of <see cref="Layers"/> + 1.</param>
/// <param name="HostBytes">Larger models: what always stays in RAM (the token embeddings), the worker's own memory and llama.cpp's host buffers.</param>
/// <param name="GpuComputeBytes">Larger models: the graphics card's compute buffer at a 512-token micro-batch, plus the recurrent state.</param>
/// <param name="ActiveExpertShare">Mixture of experts: the share of the routed experts read for each token (8 of 256 is 0.03125).</param>
public sealed record LocalModelProfile(
    string TemplateId,
    string Architecture,
    int ContextTokens,
    int MinContextTokens,
    int CpuContextTokens,
    int Layers,
    long BaseVramBytes,
    long KvBytesPerToken,
    int ChunkTokens,
    long LayerBytes = 0,
    long ExpertBytes = 0,
    long OutputBytes = 0,
    long HostBytes = 0,
    long GpuComputeBytes = 0,
    double ActiveExpertShare = 0)
{
    /// <summary>Estimated video memory for a full GPU load at <paramref name="contextTokens"/>.</summary>
    public long VramBytes(int contextTokens) => BaseVramBytes + (KvBytesPerToken * contextTokens);

    /// <summary>The profile describes where its weights are, so <see cref="MemoryPlanner"/> may split it between the card and RAM.</summary>
    public bool CanSplit => LayerBytes > 0 && Layers > 0;

    /// <summary>A mixture-of-experts model whose experts may stay in RAM while the rest runs on the card.</summary>
    public bool IsMixtureOfExperts => ExpertBytes > 0;

    /// <summary>The routed experts of one block.</summary>
    public long ExpertBytesPerLayer => Layers > 0 ? ExpertBytes / Layers : 0;

    /// <summary>The weights that may go on the card: the blocks with their experts and the output layer (the embeddings are in <see cref="HostBytes"/>).</summary>
    public long WeightBytes => (LayerBytes * Layers) + ExpertBytes + OutputBytes;
}
