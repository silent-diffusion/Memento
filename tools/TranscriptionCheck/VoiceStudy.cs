using System.Globalization;
using System.Text.Json;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Transcripts;
using Memento.Core.Voices;
using Memento.Core.Workers;
using Memento.Transcription;
using Memento.Transcription.Speakers;

namespace Memento.Tools.TranscriptionCheck;

/// <summary>
/// The known-voices threshold study (ENGINE-NOTES.md §N), offline from saved speaker jobs (<c>diarize --out</c>):
/// <list type="bullet">
/// <item>Recording A has a reference transcript whose speakers a person named. Each named person is enrolled from the
/// first half of their lines and tested with pieces of about <c>--piece</c> seconds of speech from the second half
/// (genuine tries), against every enrolled person (the best one must be right).</item>
/// <item>Recording B (optional, no names) is grouped as the app would (Auto), and pieces of its speakers are tested
/// against A's enrolled people: when the two recordings share nobody, every suggestion is a false one (impostor tries).
/// B's own speakers early against late give a second set of genuine similarities, and the app's matcher is run on B's
/// whole speakers with A's people as known voices.</item>
/// </list>
/// Then thresholds and margins are swept. Only labels (A1.., B1..) and numbers are printed, never names or words.
/// </summary>
internal static class VoiceStudy
{
    public static int Run(string aDiarize, string aTranscript, string? bDiarize, string? bTranscript, double piece)
    {
        var a = Load(aDiarize, aTranscript);
        var named = Named(aTranscript);
        var people = named.Values.Distinct(StringComparer.Ordinal)
            .Select(id => (Id: id, Talk: a.Segments.Where(s => named.GetValueOrDefault(s.Id) == id).Sum(s => s.End - s.Start)))
            .OrderByDescending(p => p.Talk)
            .Select((p, i) => (p.Id, Label: $"A{i + 1}", p.Talk))
            .ToList();
        Console.WriteLine($"A: {a.Segments.Count} lines, {a.Voices.Clusters.Count} voices that won lines, {people.Count} named people ({string.Join(", ", people.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Label} {p.Talk:0} s")))})");
        var voiceOfA = SpeakerVoices.OfLines(a.Voices);

        // Enrol each person from the first half of their lines; test pieces of the second half.
        var enrolled = new List<(string Label, VoicePrint Print)>();
        var genuine = new List<(string Truth, VoicePrint Print)>();
        foreach (var (id, label, _) in people)
        {
            var lines = a.Segments.Where(s => named.GetValueOrDefault(s.Id) == id).OrderBy(s => s.Start).ToList();
            var half = lines.Count / 2;
            if (Voice(lines.Take(half), voiceOfA) is { } early)
            {
                enrolled.Add((label, early));
            }

            foreach (var chunk in Pieces(lines.Skip(half), piece))
            {
                if (Voice(chunk, voiceOfA) is { } print)
                {
                    genuine.Add((label, print));
                }
            }

            if (Voice(lines.Take(half), voiceOfA) is { } e && Voice(lines.Skip(half), voiceOfA) is { } l)
            {
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {label}: early {e.Seconds:0} s vs late {l.Seconds:0} s of voiced speech, cosine {e.Similarity(l):0.000}"));
            }
        }

        // The pieces above share the diarizer's cluster embeddings (a voice is the mix of the clusters of its lines), so
        // early against late is optimistic. Independent tries: each cluster of a named person (10 s or more, 80 % or more
        // of its lines that person's) against that person enrolled from their other clusters, and against the others.
        var clusterTries = new List<(string Truth, double Right, double BestWrong, double Seconds)>();
        var length = a.Segments.ToDictionary(s => s.Id, s => s.End - s.Start, StringComparer.Ordinal);
        var labelOf = people.ToDictionary(p => p.Id, p => p.Label, StringComparer.Ordinal);
        var clusters = a.Voices.Clusters
            .Select(c => (Cluster: c, Print: VoicePrint.From(c.Embedding, c.Seconds), Owner: c.SegmentIds.Where(named.ContainsKey).GroupBy(id => named[id]).Select(g => (Person: g.Key, Seconds: g.Sum(id => length[id]))).OrderByDescending(g => g.Seconds).FirstOrDefault(), Total: c.SegmentIds.Sum(id => length.GetValueOrDefault(id))))
            .Where(c => c.Print is not null && c.Owner.Person is not null && c.Owner.Seconds >= 0.8 * c.Total)
            .ToList();
        foreach (var c in clusters.Where(c => c.Cluster.Seconds >= 10))
        {
            var truth = labelOf[c.Owner.Person];
            VoicePrint? Enrol(string person, bool leaveOut) => clusters
                .Where(o => o.Owner.Person == person && (!leaveOut || !ReferenceEquals(o.Cluster, c.Cluster)))
                .Select(o => o.Print!)
                .Aggregate((VoicePrint?)null, (mix, p) => mix is null ? p : mix.Combine(p) ?? mix);
            if (Enrol(c.Owner.Person, true) is not { } own)
            {
                continue;
            }

            var wrong = people.Where(p => p.Id != c.Owner.Person).Select(p => Enrol(p.Id, false)).OfType<VoicePrint>().Select(p => c.Print!.Similarity(p)).DefaultIfEmpty(-1).Max();
            clusterTries.Add((truth, c.Print!.Similarity(own), wrong, c.Cluster.Seconds));
        }

        Console.WriteLine($"Independent tries: {clusterTries.Count} clusters of named people with 10 s or more ({string.Join(", ", clusterTries.GroupBy(t => t.Truth).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => $"{g.Key} {g.Count()}"))})");
        Describe("cluster vs own person (other clusters)", clusterTries.Select(t => t.Right).ToList());
        Describe("cluster vs best other person", clusterTries.Select(t => t.BestWrong).ToList());
        Console.WriteLine("  threshold margin: suggested right / suggested wrong / none");
        foreach (var threshold in new[] { 0.5, 0.55, 0.6, 0.62, 0.65, 0.7 })
        {
            foreach (var margin in new[] { 0.0, 0.1, 0.15 })
            {
                var right = clusterTries.Count(t => t.Right >= threshold && t.Right - t.BestWrong >= margin);
                var wrong = clusterTries.Count(t => t.BestWrong >= threshold && t.BestWrong - t.Right >= margin);
                Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"    {threshold:0.00} {margin:0.00}: {right} / {wrong} / {clusterTries.Count - right - wrong}"));
            }
        }

        Console.WriteLine("A enrolled (early halves), pairwise cosine:");
        foreach (var x in enrolled)
        {
            Console.WriteLine("  " + x.Label + ": " + string.Join(" ", enrolled.Select(y => string.Create(CultureInfo.InvariantCulture, $"{y.Label} {x.Print.Similarity(y.Print):0.000}"))));
        }

        var impostors = new List<VoicePrint>();
        if (bDiarize is not null && bTranscript is not null)
        {
            var b = Load(bDiarize, bTranscript);
            var voiceOfB = SpeakerVoices.OfLines(b.Voices);
            var speakers = b.Speakers.OrderByDescending(s => s.TalkTimeMs).Select((s, i) => (s.Id, Label: $"B{i + 1}", s.TalkTimeMs)).ToList();
            Console.WriteLine($"B: {b.Segments.Count} lines, Auto grouping: {speakers.Count} speakers ({string.Join(", ", speakers.Select(s => string.Create(CultureInfo.InvariantCulture, $"{s.Label} {s.TalkTimeMs / 1000.0:0} s")))})");
            foreach (var (id, label, _) in speakers)
            {
                var lines = b.Segments.Where(s => s.Speaker == id).OrderBy(s => s.Start).ToList();
                if (Voice(lines, voiceOfB) is { } whole)
                {
                    Console.WriteLine("  " + label + " vs A people: " + string.Join(" ", enrolled.Select(p => string.Create(CultureInfo.InvariantCulture, $"{p.Label} {whole.Similarity(p.Print):0.000}"))));
                }

                var half = lines.Count / 2;
                if (Voice(lines.Take(half), voiceOfB) is { } e && Voice(lines.Skip(half), voiceOfB) is { } l)
                {
                    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {label}: early {e.Seconds:0} s vs late {l.Seconds:0} s, cosine {e.Similarity(l):0.000}"));
                }

                foreach (var chunk in Pieces(lines, piece))
                {
                    if (Voice(chunk, voiceOfB) is { } print)
                    {
                        impostors.Add(print);
                    }
                }
            }

            // The app's own matcher on B with A's people (full) as known voices.
            var known = people.Select(p => new KnownVoice
            {
                Id = "v" + p.Label,
                Name = p.Label,
                EmbeddingModelId = b.Voices.EmbeddingModelId,
                Samples = [new KnownVoiceSample("a", p.Id, Voice(a.Segments.Where(s => named.GetValueOrDefault(s.Id) == p.Id), voiceOfA)!.Direction.Select(x => (float)x).ToArray(), 60, DateTimeOffset.UnixEpoch)],
            }).ToList();
            var suggestions = VoiceMatcher.Match(b.Segments, b.Speakers, b.Voices, known, "b", []);
            Console.WriteLine($"VoiceMatcher on B with A's {known.Count} people known: {suggestions.Count} suggestions" + string.Concat(suggestions.Select(s => string.Create(CultureInfo.InvariantCulture, $"; {speakers.First(x => x.Id == s.SpeakerId).Label} → {s.Voice.Name} {s.Similarity:0.000} (margin {s.Margin:0.000})"))));
        }

        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"Tries: {genuine.Count} genuine pieces (A, about {piece:0} s each), {impostors.Count} impostor pieces (B against A's people)"));
        Describe("genuine: cosine with the right person", genuine.Select(g => g.Print.Similarity(enrolled.First(e => e.Label == g.Truth).Print)).ToList());
        Describe("genuine: cosine with the best wrong person", genuine.Select(g => enrolled.Where(e => e.Label != g.Truth).Max(e => g.Print.Similarity(e.Print))).ToList());
        if (impostors.Count > 0)
        {
            Describe("impostor: cosine with the best A person", impostors.Select(i => enrolled.Max(e => i.Similarity(e.Print))).ToList());
        }

        Console.WriteLine("threshold margin: right / wrong / none (genuine) · false (impostor)");
        foreach (var threshold in new[] { 0.45, 0.5, 0.55, 0.6, 0.62, 0.65, 0.7, 0.75, 0.8 })
        {
            foreach (var margin in new[] { 0.0, 0.05, 0.1, 0.15 })
            {
                int right = 0, wrong = 0, none = 0, falseAccept = 0;
                foreach (var (truth, print) in genuine)
                {
                    var (label, sim, gap) = Best(print, enrolled);
                    if (sim >= threshold && gap >= margin)
                    {
                        if (label == truth)
                        {
                            right++;
                        }
                        else
                        {
                            wrong++;
                        }
                    }
                    else
                    {
                        none++;
                    }
                }

                foreach (var print in impostors)
                {
                    var (_, sim, gap) = Best(print, enrolled);
                    if (sim >= threshold && gap >= margin)
                    {
                        falseAccept++;
                    }
                }

                Console.WriteLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {threshold:0.00} {margin:0.00}: {right,3} / {wrong,2} / {none,3} ({Percent(right, genuine.Count)} right, {Percent(wrong, genuine.Count)} wrong) · {falseAccept,3} of {impostors.Count} ({Percent(falseAccept, impostors.Count)})"));
            }
        }

        return 0;
    }

    /// <summary>The cosine of every voice (5 s or more) of one saved speaker job with every voice of another.</summary>
    public static int Pair(string first, string second)
    {
        static List<(string Label, VoicePrint Print)> Voices(string path, string prefix) =>
            JsonSerializer.Deserialize(File.ReadAllText(path), WorkerJsonContext.Default.WorkerReply)!.Diarization!.Tracks
                .SelectMany(t => t.Voices ?? [])
                .Where(v => v.Seconds >= 5)
                .OrderByDescending(v => v.Seconds)
                .Select((v, i) => (Label: string.Create(CultureInfo.InvariantCulture, $"{prefix}{i + 1} ({v.Seconds:0} s)"), Print: VoicePrint.From(v.Embedding, v.Seconds)))
                .Where(v => v.Print is not null)
                .Select(v => (v.Label, v.Print!))
                .ToList();
        var a = Voices(first, "X");
        var b = Voices(second, "Y");
        Console.WriteLine($"{a.Count} voices in the first, {b.Count} in the second");
        foreach (var (label, print) in a)
        {
            Console.WriteLine($"  {label}: " + string.Join(" ", b.Select(y => string.Create(CultureInfo.InvariantCulture, $"{y.Label} {print.Similarity(y.Print):0.000}"))) + " · within: " + string.Join(" ", a.Where(x => x.Label != label).Select(x => string.Create(CultureInfo.InvariantCulture, $"{x.Label} {print.Similarity(x.Print):0.000}"))));
        }

        return 0;
    }

    /// <summary>
    /// Suggested chapters (ChapterSuggester) for a transcript and its annotations' topics, with the time it took; titles
    /// only with <paramref name="showTitles"/>, since they are the recording's own words.
    /// </summary>
    public static int Chapters(string transcriptPath, string? annotationsPath, bool showTitles)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(transcriptPath));
        var segments = document.RootElement.GetProperty("segments").EnumerateArray()
            .Select(s => new TranscriptSegment(
                s.GetProperty("id").GetString()!,
                s.GetProperty("start").GetDouble(),
                s.GetProperty("end").GetDouble(),
                null,
                s.TryGetProperty("speaker", out var speaker) && speaker.ValueKind == JsonValueKind.String ? speaker.GetString() : null,
                null,
                s.GetProperty("text").GetString() ?? string.Empty,
                1,
                [],
                null))
            .ToList();
        var topics = annotationsPath is null
            ? []
            : JsonDocument.Parse(File.ReadAllText(annotationsPath)).RootElement.GetProperty("topics").EnumerateArray().Select(t => t.GetProperty("label").GetString()!).ToList();
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var suggestions = ChapterSuggester.Suggest(segments, topics);
        var elapsed = stopwatch.Elapsed.TotalMilliseconds;
        var topicTitles = suggestions.Count(s => topics.Contains(s.Title, StringComparer.Ordinal));
        var fallback = suggestions.Count(s => s.Title.EndsWith('…'));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{segments.Count} lines, {topics.Count} topics: {suggestions.Count} suggested chapters in {elapsed:0} ms; {topicTitles} titled with a topic, {fallback} with the first words"));
        for (var i = 0; i < suggestions.Count; i++)
        {
            var s = suggestions[i];
            var next = i + 1 < suggestions.Count ? suggestions[i + 1].AtMs : (long)(segments.Max(x => x.End) * 1000);
            var title = showTitles ? s.Title : $"({s.Title.Split(' ').Length} words)";
            Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {s.AtMs / 60000}:{s.AtMs / 1000 % 60:00} for {(next - s.AtMs) / 60000.0:0.0} min · {s.Basis} · {title}"));
        }

        return 0;
    }

    private static string Percent(int n, int of) => of == 0 ? "-" : string.Create(CultureInfo.InvariantCulture, $"{100.0 * n / of:0.0}%");

    private static (string Label, double Similarity, double Margin) Best(VoicePrint print, List<(string Label, VoicePrint Print)> enrolled)
    {
        var scored = enrolled.Select(e => (e.Label, Similarity: print.Similarity(e.Print))).OrderByDescending(e => e.Similarity).ToList();
        return (scored[0].Label, scored[0].Similarity, scored.Count > 1 ? scored[0].Similarity - scored[1].Similarity : 1);
    }

    private static void Describe(string what, List<double> values)
    {
        if (values.Count == 0)
        {
            return;
        }

        values.Sort();
        double At(double q) => values[(int)Math.Clamp(Math.Round(q * (values.Count - 1)), 0, values.Count - 1)];
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {what}: min {values[0]:0.000}, 5% {At(0.05):0.000}, median {At(0.5):0.000}, 95% {At(0.95):0.000}, max {values[^1]:0.000} (n {values.Count})"));
    }

    /// <summary>Consecutive runs of lines with about <paramref name="seconds"/> of speech each (the last short one dropped).</summary>
    private static IEnumerable<List<TranscriptSegment>> Pieces(IEnumerable<TranscriptSegment> lines, double seconds)
    {
        var piece = new List<TranscriptSegment>();
        var total = 0.0;
        foreach (var line in lines)
        {
            piece.Add(line);
            total += line.End - line.Start;
            if (total >= seconds)
            {
                yield return piece;
                piece = [];
                total = 0;
            }
        }
    }

    private static VoicePrint? Voice(IEnumerable<TranscriptSegment> lines, IReadOnlyDictionary<string, VoicePrint> voiceOf)
    {
        var list = lines.Select(s => s with { Speaker = "x" }).ToList();
        return SpeakerVoices.Of("x", list, voiceOf);
    }

    private sealed record Heard(IReadOnlyList<TranscriptSegment> Segments, IReadOnlyList<Speaker> Speakers, VoicesDocument Voices);

    /// <summary>The transcript's lines grouped as the speakers stage does on Auto, with the voices it would keep.</summary>
    private static Heard Load(string diarizePath, string transcriptPath)
    {
        var reply = JsonSerializer.Deserialize(File.ReadAllText(diarizePath), WorkerJsonContext.Default.WorkerReply)!;
        using var document = JsonDocument.Parse(File.ReadAllText(transcriptPath));
        var segments = new List<TranscriptSegment>();
        var trackId = "track";
        foreach (var segment in document.RootElement.GetProperty("segments").EnumerateArray())
        {
            trackId = segment.TryGetProperty("track", out var track) && track.ValueKind == JsonValueKind.String ? track.GetString()! : trackId;
            segments.Add(new TranscriptSegment(segment.GetProperty("id").GetString()!, segment.GetProperty("start").GetDouble(), segment.GetProperty("end").GetDouble(), trackId, null, null, string.Empty, 1, [], null));
        }

        var tracks = reply.Diarization!.Tracks.Select(t => t with { TrackId = trackId }).ToList();
        var assigned = SpeakerAssigner.Assign(segments, tracks, null, TranscriptionDefaults.JoinSimilarity, TranscriptionDefaults.MinSpeakerSeconds, TranscriptionDefaults.FoldSimilarity, TranscriptionDefaults.OwnSpeakerSeconds);
        return new Heard(assigned.Segments, assigned.Speakers, new VoicesDocument(VoicesDocument.CurrentSchemaVersion, "nemo-titanet-small", "study", tracks, assigned.Voices ?? []));
    }

    /// <summary>Line id → speaker id for the lines whose speaker a person named.</summary>
    private static Dictionary<string, string> Named(string path)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var named = document.RootElement.GetProperty("speakers").EnumerateArray()
            .Where(s => s.GetProperty("renamed").GetBoolean())
            .Select(s => s.GetProperty("id").GetString()!)
            .ToHashSet(StringComparer.Ordinal);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in document.RootElement.GetProperty("segments").EnumerateArray())
        {
            if (segment.TryGetProperty("speaker", out var speaker) && speaker.ValueKind == JsonValueKind.String && named.Contains(speaker.GetString()!))
            {
                map[segment.GetProperty("id").GetString()!] = speaker.GetString()!;
            }
        }

        return map;
    }
}
