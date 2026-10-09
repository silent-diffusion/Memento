using System.Globalization;
using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Workers;
using Memento.Transcription.Speakers;

namespace Memento.Tools.TranscriptionCheck;

/// <summary>
/// Scores a saved speaker job (<c>diarize --out</c>) on a transcript's lines, offline: how many speakers come out with
/// each expected count and join similarity, and, against a reference transcript whose speakers a person corrected and
/// named, how much of the named speakers' speech lands on the right speaker (one speaker per person, matched greedily by
/// shared speech) and how pure the speakers are (each speaker counted as its majority person). Only ids are printed.
/// </summary>
internal static class Evaluation
{
    public static int Run(string diarizePath, string transcriptPath, string? referencePath, IReadOnlyList<string> counts, IReadOnlyList<string> joins, IReadOnlyList<string> mins, IReadOnlyList<string> folds, double own)
    {
        var reply = JsonSerializer.Deserialize(File.ReadAllText(diarizePath), WorkerJsonContext.Default.WorkerReply)!;
        var segments = Segments(transcriptPath, out var trackId, out _);
        var tracks = reply.Diarization!.Tracks.Select(t => t with { TrackId = trackId }).ToList();
        var reference = referencePath is null ? null : Reference(referencePath);
        Console.WriteLine($"{Path.GetFileName(diarizePath)}: {tracks.Sum(t => t.Turns.Select(u => u.Speaker).Distinct().Count())} clusters from the diarizer, {tracks.Sum(t => t.Voices?.Count ?? 0)} with a voice; {segments.Count} lines");
        if (reference is not null)
        {
            Console.WriteLine($"reference: {reference.Values.Distinct().Count()} named speakers over {reference.Count} lines");
        }

        var configurations =
            from count in counts
            from similarityText in joins
            from min in mins
            from fold in folds
            select (count, similarityText, min, fold);
        foreach (var (count, similarityText, min, fold) in configurations)
        {
            int? expected = count == "auto" ? null : int.Parse(count, CultureInfo.InvariantCulture);
            double? similarity = similarityText == "none" ? null : double.Parse(similarityText, CultureInfo.InvariantCulture);
            var minSeconds = double.Parse(min, CultureInfo.InvariantCulture);
            var foldSimilarity = fold == "never" ? double.PositiveInfinity : fold == "always" ? double.NegativeInfinity : double.Parse(fold, CultureInfo.InvariantCulture);
            var result = SpeakerAssigner.Assign(segments, tracks, expected, similarity, minSeconds, foldSimilarity, own);
            var total = Math.Max(1, result.Speakers.Sum(s => s.TalkTimeMs));
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"  count {count,-4} join {similarityText,-5} min {min,-4} fold {fold,-6}: {result.Speakers.Count,3} speakers, {result.Speakers.Count(s => s.TalkTimeMs >= 0.02 * total),2} with 2%+ of the talk, {result.Speakers.Count(s => s.TalkTimeMs < 10_000),3} under 10 s");
            if (reference is not null)
            {
                var (accuracy, purity) = Score(result.Segments, reference, segments);
                line += string.Create(CultureInfo.InvariantCulture, $"; named speech right {100 * accuracy:0.0}%, purity {100 * purity:0.0}%");
            }

            line += "; shares " + string.Join(" ", result.Speakers.OrderByDescending(s => s.TalkTimeMs).Take(8).Select(s => string.Create(CultureInfo.InvariantCulture, $"{100.0 * s.TalkTimeMs / total:0.0}")));
            Console.WriteLine(line);
        }

        return 0;
    }

    /// <summary>The transcript's lines without speakers, all on its one track.</summary>
    private static List<TranscriptSegment> Segments(string path, out string trackId, out Dictionary<string, string?> speakers)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var list = new List<TranscriptSegment>();
        speakers = new Dictionary<string, string?>(StringComparer.Ordinal);
        trackId = "track";
        foreach (var segment in document.RootElement.GetProperty("segments").EnumerateArray())
        {
            var id = segment.GetProperty("id").GetString()!;
            trackId = segment.TryGetProperty("track", out var track) && track.ValueKind == JsonValueKind.String ? track.GetString()! : trackId;
            speakers[id] = segment.TryGetProperty("speaker", out var speaker) && speaker.ValueKind == JsonValueKind.String ? speaker.GetString() : null;
            list.Add(new TranscriptSegment(id, segment.GetProperty("start").GetDouble(), segment.GetProperty("end").GetDouble(), trackId, null, null, string.Empty, 1, [], null));
        }

        return list;
    }

    /// <summary>Line id → speaker id for the lines whose speaker a person named.</summary>
    private static Dictionary<string, string> Reference(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var named = document.RootElement.GetProperty("speakers").EnumerateArray()
            .Where(s => s.GetProperty("renamed").GetBoolean())
            .Select(s => s.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        Segments(path, out _, out var speakers);
        return speakers.Where(p => p.Value is not null && named.Contains(p.Value)).ToDictionary(p => p.Key, p => p.Value!, StringComparer.Ordinal);
    }

    private static (double Accuracy, double Purity) Score(IReadOnlyList<TranscriptSegment> assigned, Dictionary<string, string> reference, IReadOnlyList<TranscriptSegment> lines)
    {
        var length = lines.ToDictionary(s => s.Id, s => Math.Max(0, s.End - s.Start), StringComparer.Ordinal);
        var overlap = new Dictionary<(string Found, string Truth), double>();
        double total = 0;
        foreach (var segment in assigned)
        {
            if (!reference.TryGetValue(segment.Id, out var truth))
            {
                continue;
            }

            total += length[segment.Id];
            var key = (segment.Speaker ?? "-", truth);
            overlap[key] = overlap.GetValueOrDefault(key) + length[segment.Id];
        }

        var usedFound = new HashSet<string>(StringComparer.Ordinal);
        var usedTruth = new HashSet<string>(StringComparer.Ordinal);
        double matched = 0;
        foreach (var ((found, truth), seconds) in overlap.OrderByDescending(p => p.Value))
        {
            if (found != "-" && !usedFound.Contains(found) && !usedTruth.Contains(truth))
            {
                usedFound.Add(found);
                usedTruth.Add(truth);
                matched += seconds;
            }
        }

        var pure = overlap.Where(p => p.Key.Found != "-").GroupBy(p => p.Key.Found).Sum(g => g.Max(p => p.Value));
        return total <= 0 ? (0, 0) : (matched / total, pure / total);
    }
}
