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
/// </summary>
public static class SpeakingNowNamer
{
    /// <summary>A mark just after a turn ended still means that turn's voice, up to this long after it.</summary>
    public const double GraceSeconds = 1.0;

    /// <returns>Speaker id → the name the marks give that person.</returns>
    public static IReadOnlyDictionary<string, string> Names(
        IReadOnlyList<SpokeMark> marks,
        IReadOnlyList<string> knownNames,
        IReadOnlyList<DiarizedTrack> tracks,
        IReadOnlyList<VoiceCluster> voices,
        IReadOnlyList<TranscriptSegment> segments)
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

        // Each raw voice's person: the speaker most of its lines carry now.
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
