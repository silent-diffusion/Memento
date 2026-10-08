namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of the <c>status.footer</c> event and result of <c>status.get</c> (DESIGN.md §3 footer).</summary>
/// <param name="ProcessingPaused">Why processing is paused, in words, or <c>null</c>. M1 sends only <c>"Low disk space"</c> (<see cref="Status.FooterStatusService.LowSpaceReason"/>).</param>
public sealed record FooterStatusPayload(EngineStatus Engine, StorageStatus Storage, RecordingFooterStatus Recording, string? ProcessingPaused)
{
    /// <summary>The export running in the background (M3): "Exporting {title} · 42%".</summary>
    public ExportFooterStatus Export { get; init; } = ExportFooterStatus.Idle;

    /// <summary>The update downloading in the background (H1): "Downloading Memento 0.5.1 · 42%".</summary>
    public UpdateFooterStatus Update { get; init; } = UpdateFooterStatus.Idle;
}
