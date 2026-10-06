namespace Memento.Audio.Recording;

/// <summary>One track's level since the previous publication (BRIDGE.md <c>recording.levels</c> entry).</summary>
public sealed record TrackLevel(string SourceId, float Rms, float Peak);
