namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>annotations.suggestChapters</c>, <c>annotations.dismissSuggestion</c> and <c>annotations.restoreSuggestion</c>, in time order.</summary>
public sealed record ChapterSuggestionsResult(IReadOnlyList<ChapterSuggestion> Suggestions);
