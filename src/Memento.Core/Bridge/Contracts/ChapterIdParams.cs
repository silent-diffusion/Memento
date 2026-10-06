namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>annotations.removeChapter</c>.</summary>
public sealed record ChapterIdParams
{
    public required string RecordingId { get; init; }

    public required string ChapterId { get; init; }
}
