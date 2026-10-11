namespace Memento.Core.Projects;

/// <summary>
/// Set in <c>project.json</c> once "Delete audio" removed the recording's audio (2.1): every track file, the mix and the
/// waveform peaks. The tracks stay listed (their names, offsets and lengths, which the transcript's lines and the
/// speakers refer to) without a hash, and <see cref="ProjectManifest.Mix"/> and <see cref="ProjectManifest.Peaks"/> are
/// cleared. The transcript, documents, attachments and any video are untouched; the recording stays readable.
/// </summary>
/// <param name="At">When the files were removed.</param>
/// <param name="TrackIds">The audio tracks whose files were removed.</param>
/// <param name="BytesFreed">The size of the removed files.</param>
/// <param name="Reason"><c>user</c> (Review, the Library) or <c>age</c> (Settings › Storage › Delete audio tracks older than).</param>
public sealed record ProjectAudioRemoved(DateTimeOffset At, IReadOnlyList<string> TrackIds, long BytesFreed, string Reason)
{
    public const string ByUser = "user";

    public const string ByAge = "age";
}
