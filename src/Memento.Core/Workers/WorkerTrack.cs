namespace Memento.Core.Workers;

/// <summary>One track of a job: the file and where it sits on the recording timeline.</summary>
/// <param name="Id">Track id from the manifest (<c>mic</c>, <c>system</c>, …).</param>
/// <param name="Path">Full path of the lossless track file.</param>
/// <param name="OffsetSeconds">Where the track starts on the recording timeline.</param>
/// <param name="StartWindow">Transcription only: windows already done in an earlier, interrupted pass (resume point).</param>
public sealed record WorkerTrack(string Id, string Path, double OffsetSeconds, int StartWindow = 0);
