namespace Memento.Core.Bridge.Contracts;

/// <summary>What produced a transcript.</summary>
/// <param name="Name"><c>whisper.cpp</c>.</param>
/// <param name="Model">Catalog id, e.g. <c>large-v3-turbo</c>.</param>
/// <param name="Device">"GPU (Vulkan)" or "CPU".</param>
/// <param name="Version">Engine package version.</param>
/// <param name="DurationMs">How long the pass took.</param>
public sealed record TranscriptEngineInfo(string Name, string Model, string Device, string Version, long DurationMs);
