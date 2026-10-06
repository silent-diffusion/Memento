namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of the <c>status.footer</c> event and result of <c>status.get</c> (DESIGN.md §3 footer).</summary>
/// <param name="ProcessingPaused">Why processing is paused, in words (e.g. "Low disk space"), or <c>null</c>.</param>
public sealed record FooterStatusPayload(EngineStatus Engine, StorageStatus Storage, RecordingFooterStatus Recording, string? ProcessingPaused);
