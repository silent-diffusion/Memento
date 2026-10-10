namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>annotations.dismissSuggestion</c> and <c>annotations.restoreSuggestion</c>.</summary>
public sealed record ChapterSuggestionParams
{
    public required string RecordingId { get; init; }

    public required long AtMs { get; init; }
}
