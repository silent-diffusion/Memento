namespace Memento.Core.Projects;

/// <summary>
/// Set in <c>project.json</c> once "Remove video, keep audio" deleted the recording's video files (2.0, DESIGN.md §18.5,
/// §18.7). The video tracks stay listed (what was recorded, for Review's "Video was removed on {date}" line) without a
/// hash; audio tracks, the mix, the transcript and documents are untouched.
/// </summary>
/// <param name="At">When the files were removed.</param>
/// <param name="TrackIds">The video tracks whose files were removed.</param>
/// <param name="BytesFreed">The size of the removed files.</param>
/// <param name="Reason"><c>user</c> (Review, the Library) or <c>age</c> (Settings › Storage › Remove video older than).</param>
public sealed record ProjectVideoRemoved(DateTimeOffset At, IReadOnlyList<string> TrackIds, long BytesFreed, string Reason)
{
    public const string ByUser = "user";

    public const string ByAge = "age";
}
