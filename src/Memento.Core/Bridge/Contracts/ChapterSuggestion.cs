namespace Memento.Core.Bridge.Contracts;

/// <summary>A suggested chapter (DESIGN.md §19): where the subject changes, with a title from the words said there.</summary>
/// <param name="Id"><c>sc</c> and the time in milliseconds.</param>
/// <param name="Basis">What placed it, for the tooltip: "Topic shift · 4 s pause · new speaker".</param>
/// <param name="FromAgenda">The title and place come from one of the recording's agenda items (2.7.0).</param>
public sealed record ChapterSuggestion(string Id, long AtMs, string Title, string Basis, bool FromAgenda = false);
