namespace Memento.Video.Export;

/// <summary>A file of a track and where its time 0 sits on the output's timeline (100 ns).</summary>
internal sealed record ComposeSegment(string Path, long StartTicks);
