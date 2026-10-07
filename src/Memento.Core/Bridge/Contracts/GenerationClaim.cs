namespace Memento.Core.Bridge.Contracts;

/// <summary>One claim the pipeline checked, for "How this was made".</summary>
/// <param name="T">The transcript moment it cites, in seconds.</param>
/// <param name="Verdict"><c>supported</c>, <c>unsupported</c> or <c>notChecked</c>.</param>
/// <param name="Kept">In the document; otherwise dropped, with <see cref="Reason"/>.</param>
public sealed record GenerationClaim(string Id, string ModuleId, string Text, double? T, string Verdict, bool Kept, string? Reason);
