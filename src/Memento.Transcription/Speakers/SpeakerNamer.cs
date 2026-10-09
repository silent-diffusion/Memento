using Memento.Core.Bridge.Contracts;

namespace Memento.Transcription.Speakers;

/// <summary>
/// Names the speakers a pass found. First the names a person gave in the transcript before (when speakers are
/// identified again): each speaker the user named goes to the new speaker that took most of its lines' time (on the same
/// track), one to one, the biggest overlaps first, when that is at least a quarter of either's talk time. Then the
/// recording's known speakers (Who spoke) that are not used yet go to the remaining speakers in order of first
/// appearance. Every name given this way counts as given by the user (<c>renamed</c>), so it is listed first and kept.
/// </summary>
public static class SpeakerNamer
{
    /// <summary>How much of the smaller talk time two speakers must share for a name to carry over.</summary>
    public const double CarryOverShare = 0.25;

    /// <param name="Carried">Names kept from the previous pass.</param>
    /// <param name="Known">Names taken from the recording's known speakers.</param>
    public sealed record Result(IReadOnlyList<Speaker> Speakers, int Carried, int Known);

    public static Result Name(
        IReadOnlyList<Speaker> speakers,
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<Speaker>? previousSpeakers,
        IReadOnlyList<TranscriptSegment>? previousSegments,
        IReadOnlyList<string> knownNames)
    {
        ArgumentNullException.ThrowIfNull(speakers);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(knownNames);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var carried = 0;

        if (previousSpeakers is not null && previousSegments is not null)
        {
            var named = previousSpeakers.Where(s => s.Renamed).ToDictionary(s => s.Id, StringComparer.Ordinal);
            var talk = Talk(segments);
            var previousTalk = Talk(previousSegments);
            var shared = new Dictionary<(string New, string Old), double>();
            foreach (var old in previousSegments.Where(s => s.Speaker is not null && named.ContainsKey(s.Speaker)))
            {
                foreach (var segment in segments.Where(s => s.Speaker is not null && s.Track == old.Track && s.End > old.Start && s.Start < old.End))
                {
                    var key = (segment.Speaker!, old.Speaker!);
                    shared[key] = shared.GetValueOrDefault(key) + (Math.Min(segment.End, old.End) - Math.Max(segment.Start, old.Start));
                }
            }

            var usedOld = new HashSet<string>(StringComparer.Ordinal);
            foreach (var ((found, old), seconds) in shared.OrderByDescending(p => p.Value).ThenBy(p => p.Key.New, StringComparer.Ordinal))
            {
                var smaller = Math.Min(talk.GetValueOrDefault(found), previousTalk.GetValueOrDefault(old));
                if (names.ContainsKey(found) || usedOld.Contains(old) || seconds < CarryOverShare * smaller || seconds <= 0)
                {
                    continue;
                }

                names[found] = named[old].Name;
                usedOld.Add(old);
                carried++;
            }
        }

        var taken = names.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var unused = new Queue<string>(knownNames.Where(n => !string.IsNullOrWhiteSpace(n) && !taken.Contains(n.Trim())).Select(n => n.Trim()));
        var known = 0;
        foreach (var speaker in FirstAppearance(speakers, segments))
        {
            if (unused.Count == 0)
            {
                break;
            }

            if (!names.ContainsKey(speaker.Id))
            {
                names[speaker.Id] = unused.Dequeue();
                known++;
            }
        }

        var result = speakers.Select(s => names.TryGetValue(s.Id, out var name) ? s with { Name = name, Renamed = true } : s).ToList();
        return new Result(result, carried, known);
    }

    private static Dictionary<string, double> Talk(IReadOnlyList<TranscriptSegment> segments) =>
        segments.Where(s => s.Speaker is not null)
            .GroupBy(s => s.Speaker!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Sum(s => Math.Max(0, s.End - s.Start)), StringComparer.Ordinal);

    private static IEnumerable<Speaker> FirstAppearance(IReadOnlyList<Speaker> speakers, IReadOnlyList<TranscriptSegment> segments)
    {
        var first = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var segment in segments)
        {
            if (segment.Speaker is { } id && (!first.TryGetValue(id, out var at) || segment.Start < at))
            {
                first[id] = segment.Start;
            }
        }

        return speakers.OrderBy(s => first.GetValueOrDefault(s.Id, double.MaxValue)).ThenBy(s => s.Id, StringComparer.Ordinal);
    }
}
