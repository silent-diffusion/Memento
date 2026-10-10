namespace Memento.Core.Bridge.Contracts;

/// <summary>A suggested chapter (DESIGN.md §19): where the subject changes, with a title from the words said there.</summary>
/// <param name="Id"><c>sc</c> and the time in milliseconds.</param>
/// <param name="Basis">What placed it, for the tooltip: "Topic shift · 4 s pause · new speaker".</param>
public sealed record ChapterSuggestion(string Id, long AtMs, string Title, string Basis);
