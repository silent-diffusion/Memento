namespace Memento.Generation.Generation;

/// <summary>Where the pipeline is: <c>composing</c>, <c>generating</c>, <c>verifying</c> or <c>rendering</c>, 0–100.</summary>
public sealed record PipelineProgress(string Stage, string? ModuleId, double Percent, string? Message);
