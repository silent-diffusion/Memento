namespace Memento.Core.Bridge.Contracts;

/// <summary>What the "ask before every send" dialog shows (DESIGN.md §5.19): the provider, the inputs, the size and the chunk count.</summary>
/// <param name="InputsUsed">The ticked inputs that the Settings share switches allow: exactly what is sent.</param>
/// <param name="Bytes">UTF-8 size of the payload text.</param>
public sealed record GenerationSendSummary(string ProviderId, string ProviderName, string? ModelLabel, InputSelection InputsUsed, long Bytes, int Chunks);
