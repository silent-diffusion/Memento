using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;
using Memento.Core.Workers;

namespace Memento.Transcription.Speakers;

/// <summary>
/// Turns the Speaking now marks (<see cref="WhoSpoke.Marks"/>, 2.6.0) into names for the people a pass found. A mark
/// says that one of the known names was talking at a moment: every raw voice (track, cluster) whose diarizer turn covers
/// that moment — or, when none does, ended less than <see cref="GraceSeconds"/> before it, since the button is pressed a
/// beat after the words — votes for that name on the person its lines were grouped into (the speaker most of the voice's
/// lines carry). Each name then goes to the person with the most votes for it, the biggest tallies first (ties: the
/// person who speaks first, then the name as listed), one name per person and one person per name. A mark whose moment no
/// turn covers, or whose name is not among the known names any more, says nothing.
/// When a name was added during the recording (<see cref="WhoSpoke.Added"/>, 2.7.0), that time breaks a tie between
/// people with as many votes: the one heard after the name was added goes first. <see cref="Doubts"/> checks the result:
/// a mark that heard someone other than the person its name went to, or a name that went to someone heard only before
/// it was added, is reported as a low-confidence name.
/// </summary>
public static class SpeakingNowNamer
{
    /// <summary>A mark just after a turn ended still means that turn's voice, up to this long after it.</summary>
    public const double GraceSeconds = 1.0;

    /// <summary>A name whose evidence disagrees: <paramref name="Name"/> at <paramref name="AtMs"/>, and why.</summary>
    public sealed record Doubt(string Name, long AtMs, string Reason);

    /// <returns>Speaker id → the name the marks give that person.</returns>
    public static IReadOnlyDictionary<string, string> Names(
        IReadOnlyList<SpokeMark> marks,
        IReadOnlyList<string> knownNames,
        IReadOnlyList<DiarizedTrack> tracks,
        IReadOnlyList<VoiceCluster> voices,
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyList<SpeakerAdded>? added = null)
    {
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(knownNames);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(voices);
        ArgumentNullException.ThrowIfNull(segments);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (marks.Count == 0)
        {
            return result;
        }

        var personOf = PersonOf(voices, segments);
        var votes = new Dictionary<(string Person, string Name), int>();
        foreach (var mark in marks)
        {
            var name = knownNames.FirstOrDefault(n => string.Equals(n.Trim(), (mark.Name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is null || mark.AtMs < 0)
            {
                continue;
            }

            var at = mark.AtMs / 1000.0;
            foreach (var (track, speaker) in VoicesAt(tracks, at))
            {
                if (personOf.TryGetValue((track, speaker), out var person))
                {
                    votes[(person, name)] = votes.GetValueOrDefault((person, name)) + 1;
                }
            }
        }

        var addedAt = AddedAt(added, knownNames);
        var last = LastHeard(segments);
        var first = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var segment in segments)
        {
            if (segment.Speaker is { } id && (!first.TryGetValue(id, out var at) || segment.Start < at))
            {
                first[id] = segment.Start;
            }
        }

        var listed = knownNames.Select((n, i) => (Name: n.Trim(), Index: i)).ToDictionary(p => p.Name, p => p.Index, StringComparer.OrdinalIgnoreCase);
        var usedPeople = new HashSet<string>(StringComparer.Ordinal);
        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ((person, name), _) in votes
            .OrderByDescending(v => v.Value)
            .ThenBy(v => addedAt.TryGetValue(v.Key.Name, out var from) && last.GetValueOrDefault(v.Key.Person, double.MinValue) < from ? 1 : 0)
            .ThenBy(v => first.GetValueOrDefault(v.Key.Person, double.MaxValue))
            .ThenBy(v => v.Key.Person, StringComparer.Ordinal)
            .ThenBy(v => listed.GetValueOrDefault(v.Key.Name, int.MaxValue)))
        {
            if (usedPeople.Contains(person) || usedNames.Contains(name))
            {
                continue;
            }

            usedPeople.Add(person);
            usedNames.Add(name);
            result[person] = name;
        }

        return result;
    }

    /// <summary>
    /// The names whose evidence disagrees with where they went (<paramref name="named"/>, speaker id → name): each
    /// Speaking now mark that heard voices of other people only, and each name added during the recording that went to a
    /// person heard only before then. Ordered by time, then name.
    /// </summary>
    public static IReadOnlyList<Doubt> Doubts(
        IReadOnlyList<SpokeMark> marks,
        IReadOnlyList<SpeakerAdded> added,
        IReadOnlyList<DiarizedTrack> tracks,
        IReadOnlyList<VoiceCluster> voices,
        IReadOnlyList<TranscriptSegment> segments,
        IReadOnlyDictionary<string, string> named)
    {
        ArgumentNullException.ThrowIfNull(marks);
        ArgumentNullException.ThrowIfNull(added);
        ArgumentNullException.ThrowIfNull(tracks);
        ArgumentNullException.ThrowIfNull(voices);
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(named);
        var doubts = new List<Doubt>();
        var holder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (person, name) in named)
        {
            holder.TryAdd(name.Trim(), person);
        }

        var personOf = PersonOf(voices, segments);
        foreach (var mark in marks)
        {
            if (mark.AtMs < 0 || !holder.TryGetValue((mark.Name ?? string.Empty).Trim(), out var person))
            {
                continue;
            }

            var heard = VoicesAt(tracks, mark.AtMs / 1000.0).Select(v => personOf.GetValueOrDefault(v)).OfType<string>().ToList();
            if (heard.Count > 0 && !heard.Contains(person, StringComparer.Ordinal))
            {
                doubts.Add(new Doubt(holder.Keys.First(k => string.Equals(k, mark.Name!.Trim(), StringComparison.OrdinalIgnoreCase)), mark.AtMs, "Speaking now heard a different voice"));
            }
        }

        var last = LastHeard(segments);
        foreach (var entry in added)
        {
            if (entry.AtMs >= 0 && holder.TryGetValue((entry.Name ?? string.Empty).Trim(), out var person)
                && last.TryGetValue(person, out var end) && end * 1000 < entry.AtMs)
            {
                doubts.Add(new Doubt(entry.Name!.Trim(), entry.AtMs, "heard only before the name was added"));
            }
        }

        return doubts.OrderBy(d => d.AtMs).ThenBy(d => d.Name, StringComparer.Ordinal).ToList();
    }

    /// <summary>Each raw voice's person: the speaker most of its lines carry now.</summary>
    private static Dictionary<(string Track, int Speaker), string> PersonOf(IReadOnlyList<VoiceCluster> voices, IReadOnlyList<TranscriptSegment> segments)
    {
        var speakerOf = segments.Where(s => s.Speaker is not null).ToDictionary(s => s.Id, s => s.Speaker!, StringComparer.Ordinal);
        var personOf = new Dictionary<(string Track, int Speaker), string>();
        foreach (var voice in voices)
        {
            var person = voice.SegmentIds
                .Select(id => speakerOf.GetValueOrDefault(id))
                .OfType<string>()
                .GroupBy(id => id, StringComparer.Ordinal)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .FirstOrDefault()?.Key;
            if (person is not null)
            {
                personOf[(voice.Track, voice.Speaker)] = person;
            }
        }

        return personOf;
    }

    /// <summary>Known name → when it was added, in seconds of recorded time (the first entry per name).</summary>
    private static Dictionary<string, double> AddedAt(IReadOnlyList<SpeakerAdded>? added, IReadOnlyList<string> knownNames)
    {
        var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in added ?? [])
        {
            var name = knownNames.FirstOrDefault(n => string.Equals(n.Trim(), (entry.Name ?? string.Empty).Trim(), StringComparison.OrdinalIgnoreCase));
            if (name is not null && entry.AtMs >= 0)
            {
                result.TryAdd(name, entry.AtMs / 1000.0);
            }
        }

        return result;
    }

    /// <summary>Speaker id → when their last line ends, in seconds.</summary>
    private static Dictionary<string, double> LastHeard(IReadOnlyList<TranscriptSegment> segments)
    {
        var last = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var segment in segments)
        {
            if (segment.Speaker is { } id && (!last.TryGetValue(id, out var end) || segment.End > end))
            {
                last[id] = segment.End;
            }
        }

        return last;
    }

    /// <summary>The voices heard at <paramref name="at"/> on any track, else the ones that stopped within the grace before it.</summary>
    private static List<(string Track, int Speaker)> VoicesAt(IReadOnlyList<DiarizedTrack> tracks, double at)
    {
        var covering = new List<(string Track, int Speaker)>();
        var justBefore = new List<(string Track, int Speaker)>();
        foreach (var track in tracks)
        {
            foreach (var turn in track.Turns)
            {
                if (turn.Start <= at && at < turn.End)
                {
                    covering.Add((track.TrackId, turn.Speaker));
                }
                else if (turn.End <= at && at - turn.End < GraceSeconds)
                {
                    justBefore.Add((track.TrackId, turn.Speaker));
                }
            }
        }

        return covering.Count > 0 ? covering.Distinct().ToList() : justBefore.Distinct().ToList();
    }
}
