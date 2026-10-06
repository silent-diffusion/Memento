namespace Memento.Audio.Capture;

/// <summary>What to do with a real packet: emit <paramref name="SilenceFrames"/> zeros starting at
/// <paramref name="SilenceStartQpc"/> first, then drop <paramref name="TrimFrames"/> from its head; its first kept
/// frame is at <paramref name="KeptStartQpc"/>.</summary>
internal readonly record struct GapAdjustment(int SilenceFrames, long SilenceStartQpc, int TrimFrames, long KeptStartQpc);
