namespace Memento.Core.Bridge.Contracts;

/// <summary>An engine as the resource probe sees it now (<c>engine.status</c>, <c>status.footer.engine.detail</c>).</summary>
/// <param name="Ready">The configured model is installed and the engine can run.</param>
/// <param name="Device"><c>GPU</c> or <c>CPU</c>: where it would run now; <c>null</c> when not ready.</param>
/// <param name="GpuName">The discrete graphics card, e.g. "NVIDIA GeForce RTX 3060 Laptop GPU".</param>
/// <param name="FreeVramBytes">Free video memory on that card; <c>null</c> on CPU-only PCs.</param>
/// <param name="Model">Catalog id of the model it would use.</param>
/// <param name="Paused">Why processing is paused ("PC is busy"), or <c>null</c>.</param>
public sealed record EngineStatusDetail(bool Ready, string? Device, string? GpuName, long? FreeVramBytes, string? Model, string? Paused)
{
    /// <summary>The card's memory and who holds it (transcription only); <c>null</c> on CPU-only PCs and in <c>status.footer</c>.</summary>
    public GpuMemoryInfo? GpuMemory { get; init; }

    /// <summary>
    /// Why the model runs on the processor although there is a card (DESIGN.md §17): "The graphics card has 0.8 GB of 6 GB
    /// free. Ollama (llama-server.exe) is using 5.0 GB. Large v3 Turbo needs 2.5 GB on the card, so it runs on the
    /// processor until that memory is free. …"; <c>null</c> when it runs on the card, there is no card, and in <c>status.footer</c>.
    /// </summary>
    public string? Note { get; init; }
}
