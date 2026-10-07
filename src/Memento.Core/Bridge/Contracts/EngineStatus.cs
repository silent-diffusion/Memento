namespace Memento.Core.Bridge.Contracts;

/// <summary>Local transcription engine state in the status footer.</summary>
/// <param name="Ready">An engine and model are installed and usable.</param>
/// <param name="Device">Where it runs, <c>GPU</c> or <c>CPU</c>; <c>null</c> when no engine is ready.</param>
/// <param name="Detail">The probe result behind it (M2): model, graphics card, free video memory, pause reason.</param>
public sealed record EngineStatus(bool Ready, string? Device, EngineStatusDetail? Detail = null);
