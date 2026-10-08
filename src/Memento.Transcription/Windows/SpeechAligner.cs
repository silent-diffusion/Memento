using Memento.Core.Bridge.Contracts;
using Memento.Core.Workers;

namespace Memento.Transcription.Windows;

/// <summary>
/// Aligns a line's times to the sound under it (ENGINE-NOTES.md §K). Whisper's timestamps are coarse (often whole
/// seconds) and a line usually starts where the pause before it starts; Review highlights the line from its start, so
/// the highlight ran ahead of the voice by the length of the pause. A start that falls where nothing is heard (or in
/// the last <see cref="TailSeconds"/> of the sound before) moves to where sound next begins, and an end in silence (or
/// in the first moment of the next sound) moves back to where sound last stopped, always within the line; words are
/// kept inside the line. A line with no sound under it keeps its times.
/// </summary>
public static class SpeechAligner
{
    /// <summary>How far into the end of a sound a line's start may fall and still be taken for the line before's tail.</summary>
    public const double TailSeconds = 0.6;

    /// <param name="segment">A finished segment on the recording timeline.</param>
    /// <param name="regions">The track's sound regions (<see cref="Audio.SpeechEnergy.SoundRegions"/>) in track seconds, sorted.</param>
    /// <param name="trackOffset">Where the track starts on the recording timeline.</param>
    public static WorkerSegment Align(WorkerSegment segment, IReadOnlyList<(double Start, double End)> regions, double trackOffset)
    {
        ArgumentNullException.ThrowIfNull(segment);
        ArgumentNullException.ThrowIfNull(regions);
        if (regions.Count == 0)
        {
            return segment;
        }

        var start = segment.Start - trackOffset;
        var end = segment.End - trackOffset;

        // The first region that ends after the start: the start is in the silence before it, or in it. A start in the
        // last moment of a region (its last 0.6 s, past its middle) whose next region begins within the line is the
        // previous line's tail (Whisper's starts run up to about half a second early): the line starts with the next.
        var next = FirstEndingAfter(regions, start);
        if (next < regions.Count && regions[next].Start <= start && regions[next].End - start <= TailSeconds
            && start - regions[next].Start > regions[next].End - start
            && next + 1 < regions.Count && regions[next + 1].Start < end)
        {
            next++;
        }

        if (next < regions.Count && regions[next].Start > start && regions[next].Start < end)
        {
            start = regions[next].Start;
        }

        // The last region that starts before the end: the end is in the silence after it, or in it (and in the same
        // way an end in the first moment of a region that began after the line's sound is the next line's head).
        var previous = FirstEndingAfter(regions, end);
        if (previous >= regions.Count || regions[previous].Start >= end)
        {
            previous--;
        }
        else if (end - regions[previous].Start <= TailSeconds && end - regions[previous].Start < regions[previous].End - end
            && previous > 0 && regions[previous - 1].End > start)
        {
            previous--;
        }

        if (previous >= 0 && regions[previous].End < end && regions[previous].End > start)
        {
            end = regions[previous].End;
        }

        var newStart = Round(start + trackOffset);
        var newEnd = Round(Math.Max(start, end) + trackOffset);
        if (newStart == segment.Start && newEnd == segment.End)
        {
            return segment;
        }

        var words = segment.Words.Select(w =>
        {
            var s = Math.Clamp(w.S, newStart, newEnd);
            return w with { S = s, E = Math.Clamp(w.E, s, newEnd) };
        }).ToList();
        return segment with { Start = newStart, End = newEnd, Words = words };
    }

    /// <summary>Index of the first region whose end is after <paramref name="time"/> (regions.Count when none).</summary>
    private static int FirstEndingAfter(IReadOnlyList<(double Start, double End)> regions, double time)
    {
        int lo = 0, hi = regions.Count - 1, found = regions.Count;
        while (lo <= hi)
        {
            var mid = (lo + hi) >> 1;
            if (regions[mid].End > time)
            {
                found = mid;
                hi = mid - 1;
            }
            else
            {
                lo = mid + 1;
            }
        }

        return found;
    }

    private static double Round(double value) => Math.Round(value, 3);
}
