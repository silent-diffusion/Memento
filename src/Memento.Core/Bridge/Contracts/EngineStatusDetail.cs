namespace Memento.Core.Bridge.Contracts;

/// <summary>An engine as the resource probe sees it now (<c>engine.status</c>, <c>status.footer.engine.detail</c>).</summary>
/// <param name="Ready">The configured model is installed and the engine can run.</param>
/// <param name="Device"><c>GPU</c> or <c>CPU</c>: where it would run now; <c>null</c> when not ready.</param>
/// <param name="GpuName">The discrete graphics card, e.g. "NVIDIA GeForce RTX 3060 Laptop GPU".</param>
/// <param name="FreeVramBytes">Free video memory on that card; <c>null</c> on CPU-only PCs.</param>
/// <param name="Model">Catalog id of the model it would use.</param>
/// <param name="Paused">Why processing is paused ("PC is busy"), or <c>null</c>.</param>
public sealed record EngineStatusDetail(bool Ready, string? Device, string? GpuName, long? FreeVramBytes, string? Model, string? Paused);
