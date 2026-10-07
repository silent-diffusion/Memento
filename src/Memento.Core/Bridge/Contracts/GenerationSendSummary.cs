namespace Memento.Core.Bridge.Contracts;

/// <summary>What the "ask before every send" dialog shows (DESIGN.md §5.19): provider, inputs, size and chunk count.</summary>
/// <param name="Inputs">The included sections ("Transcript (312 segments, 4 speakers)").</param>
public sealed record GenerationSendSummary(string ProviderId, string ProviderName, string? ModelLabel, IReadOnlyList<string> Inputs, long Bytes, int Chunks, bool StaysOnPc);
