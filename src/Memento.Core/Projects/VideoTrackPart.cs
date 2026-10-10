namespace Memento.Core.Projects;

/// <summary>One file of a video track (<c>tracks/screen.mp4</c>, then <c>tracks/screen.part2.mp4</c> after a hardware encoder failure).</summary>
/// <param name="File">Relative to the project folder.</param>
/// <param name="StartOffsetMs">Its first frame on the recording timeline.</param>
public sealed record VideoTrackPart(string File, long StartOffsetMs, long DurationMs, string Encoder, bool HardwareEncoder, long FramesWritten);
