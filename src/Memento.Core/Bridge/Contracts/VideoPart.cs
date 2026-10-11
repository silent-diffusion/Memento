namespace Memento.Core.Bridge.Contracts;

/// <summary>One file of a video track, written by one encoder.</summary>
/// <param name="Url"><c>https://library.memento/&lt;id&gt;/tracks/screen.mp4</c>; <c>null</c> while the file cannot be read yet (recording, being repaired or joined) or once removed.</param>
/// <param name="StartOffsetMs">Where the part begins on the recording timeline (the file itself starts at 0).</param>
public sealed record VideoPart(string? Url, long StartOffsetMs, long DurationMs);
