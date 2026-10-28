namespace Memento.Core.Bridge.Contracts;

/// <summary>What the "ask before every send" dialog shows for a question (2.9.0): where it goes, what goes with it and the size.</summary>
/// <param name="Bytes">UTF-8 size of the whole request text (instructions, earlier turns, material and question).</param>
public sealed record AskSendSummary(string ProviderId, string ProviderName, string? ModelLabel, AskContextInfo Context, long Bytes);