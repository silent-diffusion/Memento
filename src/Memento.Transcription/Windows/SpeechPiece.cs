namespace Memento.Transcription.Windows;

/// <summary>
/// One stretch of a window's audio that is kept for the engine: <see cref="SourceStart"/>–<see cref="SourceEnd"/> in
/// seconds from the start of the window, placed at <see cref="PackedStart"/> seconds in the packed audio.
/// </summary>
public sealed record SpeechPiece(double SourceStart, double SourceEnd, double PackedStart)
{
    public double PackedEnd => PackedStart + (SourceEnd - SourceStart);
}
