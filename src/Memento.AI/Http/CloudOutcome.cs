namespace Memento.AI.Http;

/// <summary>A completed cloud request with its timings.</summary>
internal sealed record CloudOutcome(CloudStreamResult Result, AiTimings Timings);
