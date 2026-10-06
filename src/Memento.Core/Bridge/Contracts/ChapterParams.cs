namespace Memento.Core.Bridge.Contracts;

/// <summary>Parameters of <c>annotations.addChapter</c> and <c>annotations.updateChapter</c>.</summary>
public sealed record ChapterParams
{
    public required string RecordingId { get; init; }

    public required ChapterPatch Chapter { get; init; }
}
