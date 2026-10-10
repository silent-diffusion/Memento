namespace Memento.Core.Video;

/// <summary>One video file of an export (DESIGN.md §18.4).</summary>
/// <param name="Video">The track whose picture fills the file (the screen, or the camera on its own).</param>
/// <param name="Inset">The camera, burnt into <see cref="Video"/> where the inset was while recording; <c>null</c> for none.</param>
/// <param name="AudioPath">The mix, encoded as AAC into the file; <c>null</c> for a picture-only file.</param>
/// <param name="DurationMs">The file's length on the recording timeline (the mix's length when it has audio).</param>
/// <param name="Reencode">Encode the picture again (MP4); otherwise copy the recorded frames untouched ("Original container").</param>
/// <param name="Pip">The inset's position over the recording (<see cref="VideoPipChange"/>), first entry at 0.</param>
public sealed record VideoComposeRequest(
    string OutputPath,
    VideoComposeInput Video,
    VideoComposeInput? Inset,
    string? AudioPath,
    long DurationMs,
    bool Reencode,
    IReadOnlyList<VideoPipChange> Pip,
    int AudioBitrateKbps = 192);
