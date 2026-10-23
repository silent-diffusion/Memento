using Memento.Core.Bridge.Contracts;

namespace Memento.Transcription.Speakers;

/// <summary>
/// Names the speakers a pass found. First the names a person gave in the transcript before (when speakers are
/// identified again): each speaker the user named goes to the new speaker that took most of its lines' time (on the same
/// track), one to one, the biggest overlaps first, when that is at least a quarter of either's talk time. Then the
/// recording's known speakers (Who spoke) that are not used yet go to the remaining speakers in order of first
/// appearance. Every name given this way counts as given by the user (<c>renamed</c>), so it is listed first and kept.
/// Before either, a person the user's placed lines pin to a name they gave (<paramref name="pinnedNames"/>, 2.6.0:
/// <see cref="SpeakerAssigner"/>) is certain and takes that name first; it counts as kept from before. Between the names
/// kept from before and the known names by order, the Speaking now marks (<paramref name="markedNames"/>, 2.6.0:
/// <see cref="SpeakingNowNamer"/>) name the people the user pointed at while recording.
/// </summary>
public static class SpeakerNamer
{
    /// <summary>How much of the smaller talk time two speakers must share for a name to carry over.</summary>
    public const double CarryOverShare = 0.25;

    /// <param name="Carried">Names kept from the previous pass (pinned by placed lines, or carried by shared lines).</param>
    /// <param name="Known">Names taken from the recording's known speakers by order of first appearance.</param>
    /// <param name="Marked">Names given by Speaking now marks.</param>
    public sealed record Result(IReadOnlyList<Speaker> Speakers, int Carried, int Known, int Marked = 0);

    /// <param name="pinnedNames">Speaker id → the name the user gave, for people pinned by placed lines; <c>null</c> pins none.</param>
    /// <param name="markedNames">Speaker id → the name Speaking now marks give that person; <c>null</c> marks none.</param>
    public static Result Name(
        IReadOnlyList<Speaker> speakers,
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<Speaker>? previousSpeakers,
        IReadOnlyList<TranscriptSegment>? previousSegments,
        IReadOnlyList<string> knownNames,
        IReadOnlyDictionary<string, string>? pinnedNames = null,
        IReadOnlyDictionary<string, string>? markedNames = null)
    {
        ArgumentNullException.ThrowIfNull(speakers);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(knownNames);
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var carried = 0;

        // A pinned name is certain: the user put those lines on that person. It beats the overlap heuristic and is never
        // given to a second speaker by it.
        foreach (var (id, name) in (pinnedNames ?? new Dictionary<string, string>(StringComparer.Ordinal)).OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (speakers.Any(s => s.Id == id) && !names.ContainsKey(id) && usedNames.Add(name))
            {
                names[id] = name;
                carried++;
            }
        }

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
                if (names.ContainsKey(found) || usedOld.Contains(old) || usedNames.Contains(named[old].Name) || seconds < CarryOverShare * smaller || seconds <= 0)
                {
                    continue;
                }

                names[found] = named[old].Name;
                usedOld.Add(old);
                usedNames.Add(named[old].Name);
                carried++;
            }
        }

        // A Speaking now mark is the user's word on who was talking at a moment: after the names already kept, before the
        // remaining names go by order of first appearance, and never a name already given.
        var marked = 0;
        foreach (var speaker in FirstAppearance(speakers, segments))
        {
            if (markedNames is not null && markedNames.TryGetValue(speaker.Id, out var markedName) && !names.ContainsKey(speaker.Id) && usedNames.Add(markedName))
            {
                names[speaker.Id] = markedName;
                marked++;
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
        return new Result(result, carried, known, marked);
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
