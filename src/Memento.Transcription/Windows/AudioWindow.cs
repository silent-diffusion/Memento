namespace Memento.Transcription.Windows;

/// <summary>
/// One window of a track: the audio <see cref="Start"/>–<see cref="End"/> (seconds in the track) is transcribed, and
/// words starting in <see cref="KeepFrom"/>–<see cref="KeepTo"/> are kept, so the overlap with each neighbour is cut in
/// the middle and every word is kept exactly once.
/// </summary>
public sealed record AudioWindow(int Index, double Start, double End, double KeepFrom, double KeepTo);
