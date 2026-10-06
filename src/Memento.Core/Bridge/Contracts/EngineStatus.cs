namespace Memento.Core.Bridge.Contracts;

/// <summary>Local transcription engine state.</summary>
/// <param name="Ready">An engine and model are installed and usable.</param>
/// <param name="Device">Where it runs, e.g. <c>GPU</c> or <c>CPU</c>; <c>null</c> when no engine is ready.</param>
public sealed record EngineStatus(bool Ready, string? Device);
