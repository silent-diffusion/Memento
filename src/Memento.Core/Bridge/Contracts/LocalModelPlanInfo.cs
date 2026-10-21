namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// After 2.2: how a larger local model would be placed on this PC now (BRIDGE.md "Larger models"), shown in Settings
/// before it is first used and on the Builder's Local card.
/// </summary>
/// <param name="ModelId">The catalog id.</param>
/// <param name="Fits">It fits in RAM and video memory with what the rest of the PC keeps.</param>
/// <param name="Line">"Runs 28 of 64 layers on the graphics card, the rest in RAM · about 6 tokens/s · 18 GB of RAM while writing", or the §17 sentence when it does not fit.</param>
/// <param name="Placement"><c>card</c>, <c>split</c>, <c>experts</c>, <c>ram</c> or <c>none</c>.</param>
/// <param name="GpuLayers">llama.cpp's <c>n_gpu_layers</c> (blocks on the card plus the output layer).</param>
/// <param name="CardBlocks">Transformer blocks on the card.</param>
/// <param name="TotalLayers">The model's transformer blocks.</param>
/// <param name="CpuExpertLayers">Mixture of experts: blocks whose experts stay in RAM.</param>
/// <param name="ContextTokens">The context it loads with.</param>
/// <param name="VramBytes">Planned video memory.</param>
/// <param name="RamBytes">Planned RAM while writing.</param>
/// <param name="TokensPerSecond">Estimated (or, after a load, measured) writing speed.</param>
public sealed record LocalModelPlanInfo(
    string ModelId,
    bool Fits,
    string Line,
    string Placement,
    int GpuLayers,
    int CardBlocks,
    int TotalLayers,
    int CpuExpertLayers,
    int ContextTokens,
    long VramBytes,
    long RamBytes,
    double TokensPerSecond);
