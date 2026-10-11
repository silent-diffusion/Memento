using System.Diagnostics;
using System.Text.Json;
using Memento.Core.Audio;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Processing;
using Memento.Core.Library;
using Memento.Core.Projects;
using Memento.Core.Transcripts;
using Memento.Core.Workers;
using Memento.Transcription.Audio;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Tools.TranscriptionCheck;

/// <summary>
/// The level check (ENGINE-NOTES.md §Q): a recording with a loud system-audio track and a quiet microphone track
/// (<c>tools/e2e/levels-fixture.ps1</c>), each with its truth, through the real stages; per track, how much of the
/// speech has a word within 1 s (the coverage measure of §K), how many lines were heard, how far line starts are from
/// the voice, and how much speech the energy pass counted as speech and as sound (what the silence shortener keeps).
/// </summary>
internal sealed partial class Check
{
    public async Task<int> LevelsAsync(string folder, string? modelId, bool cpu, bool analyseOnly = false)
    {
        await ConfigureAsync(modelId);
        var tracks = new[] { ("system", "system", "System audio", Path.Combine(folder, "system.wav")), ("mic", "microphone", "Microphone", Path.Combine(folder, "mic.wav")) };
        foreach (var (id, _, _, wav) in tracks)
        {
            Analyse(id, wav);
        }

        if (analyseOnly)
        {
            return 0;
        }

        var created = await Store.CreateAsync(new ProjectCreateRequest("Level check", "meeting", DateTimeOffset.Now, ProjectStates.Finalizing), CancellationToken.None);
        var project = Store.GetProjectFolder(created.Id);
        var manifestTracks = new List<ProjectTrack>();
        long durationMs = 0;
        foreach (var (id, kind, name, wav) in tracks)
        {
            var target = Path.Combine(project, "tracks", id + ".wav");
            File.Copy(wav, target);
            using var reader = new WavReader(target);
            var ms = reader.TotalFrames * 1000 / reader.Format.SampleRate;
            durationMs = Math.Max(durationMs, ms);
            manifestTracks.Add(new ProjectTrack
            {
                Id = id,
                SourceId = kind + ":file",
                SourceKind = kind,
                Name = name,
                File = $"tracks/{id}.wav",
                SampleRate = reader.Format.SampleRate,
                Channels = reader.Format.Channels,
                BitsPerSample = reader.Format.BitsPerSample,
                Codec = "wav",
                DurationMs = ms,
                Sha256 = await Sha256Async(target),
            });
        }

        File.Copy(tracks[0].Item4, Path.Combine(project, "mix.wav"));
        await _services.GetRequiredService<ProjectCatalog>().UpdateAsync(
            created.Id,
            m => m with
            {
                State = ProjectStates.Ready,
                DurationMs = durationMs,
                Tracks = manifestTracks,
                Mix = new ProjectMix("mix.wav", "wav", manifestTracks[0].SampleRate, manifestTracks[0].Channels, durationMs, manifestTracks[0].Sha256!),
                Stages = [new StageStatus(StageNames.Stored, StageStates.Done, null, "Done")],
                Processing = new ProcessingRequest(ForceCpu: cpu),
            },
            CancellationToken.None);

        var stopwatch = Stopwatch.StartNew();
        await Processing.EnqueueAfterStoredAsync(created.Id, CancellationToken.None);
        await Task.Delay(200);
        await Processing.WhenIdleAsync();
        Console.WriteLine($"pipeline (transcript + speakers + topics) took {stopwatch.Elapsed.TotalSeconds:0.0} s");
        var transcript = await Transcripts.LoadAsync(created.Id, CancellationToken.None);
        if (transcript is null)
        {
            Console.WriteLine("no transcript");
            await ShowAsync(created.Id);
            return 1;
        }

        Console.WriteLine($"engine: {transcript.Engine.Model} on {transcript.Engine.Device}, pass {transcript.Engine.DurationMs / 1000.0:0.0} s");
        foreach (var (id, _, _, wav) in tracks)
        {
            Score(id, wav, transcript);
        }

        var speakersByTrack = transcript.Segments.GroupBy(s => s.Track ?? "-").Select(g => $"{g.Key}: {string.Join("/", g.Select(s => s.Speaker ?? "-").Distinct())}");
        Console.WriteLine($"speakers by track: {string.Join("; ", speakersByTrack)}");
        return await ShowAsync(created.Id);
    }

    private static List<(string Text, double Start, double End)> Truth(string wav)
    {
        using var truth = JsonDocument.Parse(File.ReadAllText(wav + ".json"));
        return truth.RootElement.GetProperty("lines").EnumerateArray()
            .Select(l => (l.GetProperty("text").GetString()!, l.GetProperty("start").GetDouble(), l.GetProperty("end").GetDouble()))
            .ToList();
    }

    /// <summary>What the energy pass sees in a track (the worker's own code on the same 16 kHz mono audio).</summary>
    private static void Analyse(string id, string wav)
    {
        var samples = Read16k(wav);
        var energy = new SpeechEnergy();
        energy.Add(samples);
        var speech = energy.Regions();
        var sound = energy.SoundRegions();
        var lines = Truth(wav);
        double total = 0, inSpeech = 0, inSound = 0;
        foreach (var (_, start, end) in lines)
        {
            for (var t = start; t < end; t += 0.05)
            {
                total += 0.05;
                inSpeech += speech.Any(r => t >= r.Start && t <= r.End) ? 0.05 : 0;
                inSound += sound.Any(r => t >= r.Start - 0.4 && t <= r.End + 0.4) ? 0.05 : 0;
            }
        }

        var peak = samples.Length == 0 ? 0 : samples.Max(MathF.Abs);
        var level = SpeechLevel.Measure(samples, 16_000);
        Console.WriteLine($"{id}: peak {Db(peak):0.0} dBFS, active speech level {Db(level.ActiveRms):0.0} dBFS, noise floor {Db(level.NoiseRms):0.0} dBFS; " +
            $"speech regions {speech.Count} ({speech.Sum(r => r.End - r.Start):0.0} s, silent track: {SpeechEnergy.IsSilent(speech)}), sound regions {sound.Count} ({sound.Sum(r => r.End - r.Start):0.0} s)");
        var frames = new List<double>();
        for (var i = 0; i + 1600 <= samples.Length; i += 1600)
        {
            double sum = 0;
            for (var j = i; j < i + 1600; j++)
            {
                sum += samples[j] * (double)samples[j];
            }

            frames.Add(Db(Math.Sqrt(sum / 1600)));
        }

        frames.Sort();
        Console.WriteLine($"{id}: 100 ms frames p10 {frames[frames.Count / 10]:0.0}, p50 {frames[frames.Count / 2]:0.0}, p75 {frames[frames.Count * 3 / 4]:0.0}, p90 {frames[frames.Count * 9 / 10]:0.0}, p99 {frames[frames.Count * 99 / 100]:0.0}, max {frames[^1]:0.0} dBFS");
        Console.WriteLine($"{id}: of {total:0.0} s of true speech, {100 * inSpeech / total:0.0}% inside speech regions, {100 * inSound / total:0.0}% kept by the silence shortener ({total - inSound:0.0} s cut)");
        foreach (var (start, end) in sound.Take(12))
        {
            Console.Write($" [{start:0.00}-{end:0.00}]");
        }

        Console.WriteLine(sound.Count > 12 ? " …" : string.Empty);
    }

    private static void Score(string id, string wav, TranscriptDocument transcript)
    {
        var lines = Truth(wav);
        var segments = transcript.Segments.Where(s => s.Track == id).ToList();
        var words = segments.SelectMany(s => s.Words.Count > 0 ? s.Words.Select(w => (w.S, w.E)) : [(s.Start, s.End)]).ToList();
        double total = 0, covered = 0;
        var heard = 0;
        var offsets = new List<double>();
        foreach (var (text, start, end) in lines)
        {
            for (var t = start; t < end; t += 0.05)
            {
                total += 0.05;
                covered += words.Any(w => t >= w.S - 1 && t <= w.E + 1) ? 0.05 : 0;
            }

            var first = segments.Where(s => s.Start < end && s.End > start - 1).MinBy(s => Math.Abs(s.Start - start));
            if (first is not null && SharesWords(first.Text, text))
            {
                heard++;
                offsets.Add(first.Start - start);
            }
            else
            {
                Console.WriteLine($"  {id} missed at {start:0.00}: \"{text}\"{(first is null ? string.Empty : $" (nearest: \"{first.Text}\" at {first.Start:0.00})")}");
            }
        }

        // Word recall: the truth's words found in the track's text (each counted as often as it is said and written).
        static List<string> Bag(IEnumerable<string> texts) => texts.SelectMany(t => t.ToLowerInvariant().Split([' ', ',', '.', '?', '!', '-'], StringSplitOptions.RemoveEmptyEntries)).ToList();
        var written = Bag(segments.Select(s => s.Text)).GroupBy(w => w).ToDictionary(g => g.Key, g => g.Count());
        var said = Bag(lines.Select(l => l.Text));
        var recalled = said.GroupBy(w => w).Sum(g => Math.Min(g.Count(), written.GetValueOrDefault(g.Key)));
        Console.WriteLine($"{id}: word recall {100.0 * recalled / Math.Max(1, said.Count):0.0}% ({recalled} of {said.Count} words)");
        var abs = offsets.Select(Math.Abs).Order().ToList();
        Console.WriteLine($"{id}: coverage {100 * covered / total:0.0}% of {total:0.0} s of speech has a word within 1 s; {heard} of {lines.Count} lines heard; {segments.Count} segments on the track; " +
            (abs.Count == 0 ? "no line times" : $"line start − voice start: median {abs[abs.Count / 2]:0.00} s, p90 {abs[(int)(abs.Count * 0.9)]:0.00} s, max {abs[^1]:0.00} s"));
    }

    /// <summary>
    /// The live draft against each track's truth (when a track has a <c>.json</c> beside it): a line counts as heard when a
    /// draft line from the same windows shares at least three of its words.
    /// </summary>
    private static void ScoreLive(IReadOnlyList<LiveTrackSource> sources, IReadOnlyList<WorkerSegment> heard, double seconds)
    {
        foreach (var source in sources)
        {
            var wav = Path.Combine(source.ProjectFolder, source.File);
            if (!File.Exists(wav + ".json"))
            {
                continue;
            }

            var lines = Truth(wav).Where(l => l.End <= seconds).ToList();
            var found = lines.Count(l => heard.Any(s => s.Start < l.End + 10 && s.End > l.Start - 10 && SharedWords(s.Text, l.Text) >= 0.5));
            Console.WriteLine($"live, {source.File}: {found} of {lines.Count} lines heard in the draft");
        }
    }

    /// <summary>The share of the truth line's words of four letters or more that the transcribed line has.</summary>
    private static double SharedWords(string transcribed, string truth)
    {
        static HashSet<string> Words(string s) => s.ToLowerInvariant().Split([' ', ',', '.', '?', '!'], StringSplitOptions.RemoveEmptyEntries).Where(w => w.Length >= 4).ToHashSet();
        var said = Words(truth);
        return said.Count == 0 ? 0 : Words(transcribed).Count(said.Contains) / (double)said.Count;
    }

    private static bool SharesWords(string transcribed, string truth)
    {
        static HashSet<string> Words(string s) => s.ToLowerInvariant().Split([' ', ',', '.', '?', '!'], StringSplitOptions.RemoveEmptyEntries).ToHashSet();
        var said = Words(truth);
        return Words(transcribed).Count(said.Contains) >= 2;
    }

    private static double Db(double linear) => 20 * Math.Log10(Math.Max(1e-9, linear));

    /// <summary>The WAV as 16 kHz mono (a box average over each 16 kHz sample's input samples; the fixture is 48 kHz).</summary>
    private static float[] Read16k(string wav)
    {
        using var reader = new WavReader(wav);
        var channels = reader.Format.Channels;
        var interleaved = new float[reader.TotalFrames * channels];
        var frames = reader.ReadFrames(interleaved);
        var step = reader.Format.SampleRate / 16_000;
        var output = new float[frames / step];
        for (var i = 0; i < output.Length; i++)
        {
            double sum = 0;
            for (var f = i * step; f < (i + 1) * step; f++)
            {
                for (var c = 0; c < channels; c++)
                {
                    sum += interleaved[(f * channels) + c];
                }
            }

            output[i] = (float)(sum / (step * channels));
        }

        return output;
    }
}
