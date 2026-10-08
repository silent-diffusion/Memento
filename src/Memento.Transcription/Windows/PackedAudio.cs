using Memento.Transcription.Words;

namespace Memento.Transcription.Windows;

/// <summary>
/// One chunk of a window's audio as the engine hears it (<see cref="SpeechPacker"/>): pieces of the window placed one
/// after the other, and the map from times in it back to the window's own time. A time on the border of two pieces is
/// the end of one and the start of the next: a start maps to the later piece, an end to the earlier one.
/// </summary>
public sealed class PackedAudio
{
    public PackedAudio(float[] samples, IReadOnlyList<SpeechPiece> pieces)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(pieces);
        if (pieces.Count == 0)
        {
            throw new ArgumentException("Packed audio needs at least one piece.", nameof(pieces));
        }

        Samples = samples;
        Pieces = pieces;
    }

    /// <summary>The audio the engine transcribes.</summary>
    public float[] Samples { get; }

    /// <summary>The kept stretches in order, covering the packed audio from 0 without a gap.</summary>
    public IReadOnlyList<SpeechPiece> Pieces { get; }

    /// <summary>Seconds of packed audio.</summary>
    public double Seconds => Pieces[^1].PackedEnd;

    /// <summary>Window time of <paramref name="packed"/> seconds when it starts something (a line, a word).</summary>
    public double ToWindowStart(double packed) => Map(packed, end: false);

    /// <summary>Window time of <paramref name="packed"/> seconds when it ends something.</summary>
    public double ToWindowEnd(double packed) => Map(packed, end: true);

    /// <summary>The engine's segment with its times (and its tokens' times) moved from the packed audio to the window.</summary>
    public RawSegment ToWindow(RawSegment segment)
    {
        ArgumentNullException.ThrowIfNull(segment);
        var start = ToWindowStart(segment.Start);
        return segment with
        {
            Start = start,
            End = Math.Max(start, ToWindowEnd(segment.End)),
            Tokens = segment.Tokens.Select(t => t with { Start = ToWindowStart(t.Start), End = ToWindowEnd(t.End) }).ToList(),
        };
    }

    private double Map(double packed, bool end)
    {
        // The first piece that ends after the time (an end on a border stays in the piece it ends).
        int lo = 0, hi = Pieces.Count - 1, found = Pieces.Count - 1;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            var pieceEnd = mid + 1 < Pieces.Count ? Pieces[mid + 1].PackedStart : Pieces[mid].PackedEnd;
            if (end ? packed <= pieceEnd : packed < pieceEnd)
            {
                found = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        var p = Pieces[found];
        return p.SourceStart + Math.Clamp(packed - p.PackedStart, 0, p.SourceEnd - p.SourceStart);
    }
}
