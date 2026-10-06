using System.Diagnostics;
using System.Globalization;
using Memento.Audio;
using Memento.Audio.Codecs;
using Memento.Audio.Mixing;
using Memento.Audio.Recording;
using Memento.Audio.Sources;
using Memento.Audio.Writing;

namespace Memento.Tools.AudioCheck;

internal static class Checks
{
    public static async Task<int> RecordAsync(string outDir, int seconds, int checkpointSeconds)
    {
        var tracksDir = Path.Combine(outDir, "tracks");
        Directory.CreateDirectory(tracksDir);
        Console.WriteLine($"output: {outDir}");
        using var player = TonePlayer.Start(outDir, seconds + 20);
        var (mic, system, app) = await ResolveSourcesAsync(player.ProcessId);
        Console.WriteLine($"sources: mic '{mic.Name}', system '{system.Name}' ({system.Detail}), app '{app.Name}' pid {app.ProcessId}");

        var levelEvents = 0;
        var peaks = new Dictionary<string, float>();
        var wall = Stopwatch.StartNew();
        await using var session = await AudioRecordingSession.StartAsync(
            new AudioRecordingOptions(tracksDir, [mic.Id, system.Id, app.Id]) { CheckpointInterval = TimeSpan.FromSeconds(checkpointSeconds) },
            CancellationToken.None);
        session.Checkpointed += (_, e) => Console.WriteLine(
            $"  checkpoint at {e.Checkpoint.Elapsed.TotalSeconds:0.000} s: "
            + string.Join("; ", e.Checkpoint.Tracks.Select(t => $"{t.FileStem} {t.Duration.TotalSeconds:0.000} s durable, drift {Ppm(t.DriftPpm)}, overrun {t.OverrunFrames}")));
        session.SourceLost += (_, e) => Console.WriteLine($"  SOURCE LOST: {e.Message}");
        session.Levels += (_, e) =>
        {
            Interlocked.Increment(ref levelEvents);
            lock (peaks)
            {
                foreach (var l in e.Levels)
                {
                    peaks[l.SourceId] = Math.Max(peaks.GetValueOrDefault(l.SourceId), l.Peak);
                }
            }
        };

        await Task.Delay(TimeSpan.FromSeconds(seconds));
        var result = await session.StopAsync();
        wall.Stop();
        Console.WriteLine($"stopped: timeline {result.Duration.TotalSeconds:0.000} s, wall clock start→stop {wall.Elapsed.TotalSeconds:0.000} s, level events {levelEvents} ({levelEvents / result.Duration.TotalSeconds:0.0}/s)");
        foreach (var t in result.Tracks)
        {
            var bytes = t.Parts.Sum(p => new FileInfo(p).Length);
            var clockDiffMs = (t.Duration - result.Duration).TotalMilliseconds;
            Console.WriteLine($"  {t.FileStem,-16} {t.Kind,-11} capture [{t.CaptureFormat}] → stored [{t.StorageFormat}]");
            Console.WriteLine($"  {string.Empty,-16} frames {t.Frames:N0} = {t.Duration.TotalSeconds:0.000} s vs timeline {result.Duration.TotalSeconds:0.000} s ({clockDiffMs:+0.0;-0.0} ms), start offset {t.StartOffset.TotalMilliseconds:0} ms, drift {Ppm(t.DriftPpm)}");
            Console.WriteLine($"  {string.Empty,-16} WAV {bytes:N0} B in {t.Parts.Count} part(s); packets {t.Statistics.Packets:N0}, silent {t.Statistics.SilentPackets:N0}, synthesized {t.Statistics.SynthesizedFrames:N0} frames, trimmed {t.Statistics.TrimmedFrames:N0}, overrun {t.Statistics.OverrunFrames:N0}, discontinuities {t.Statistics.Discontinuities}, timestamp errors {t.Statistics.TimestampErrors}, peak level {peaks.GetValueOrDefault(t.SourceId):0.000}");
        }

        var flac = new MediaFoundationFlacEncoder();
        foreach (var t in result.Tracks)
        {
            var set = WavTrackSet.FromParts(t.Parts);
            var sw = Stopwatch.StartNew();
            var encoded = await flac.EncodeAsync(set, Path.Combine(tracksDir, t.FileStem + ".flac"), CancellationToken.None);
            Console.WriteLine($"  FLAC {t.FileStem,-16} {encoded.Bytes:N0} B ({100.0 * encoded.Bytes / set.TotalDataBytes:0.0}% of WAV) in {sw.ElapsedMilliseconds} ms incl. bit-exact verify, sha256 {encoded.Sha256[..12]}…");
        }

        foreach (var t in result.Tracks.Where(t => t.Kind != AudioSourceKind.Microphone))
        {
            Console.WriteLine($"  tone 440 Hz in {t.FileStem}: {ToneDbfs(WavTrackSet.FromParts(t.Parts)):0.0} dBFS (player writes −20 dBFS)");
        }

        var mix = await TrackMixer.MixAsync(
            result.Tracks.Select(t => MixInput.FromTrack(WavTrackSet.FromParts(t.Parts), t.StartOffset, t.EndedEarlyAt)).ToList(),
            outDir,
            "mix",
            CancellationToken.None);
        var mixFlac = await flac.EncodeAsync(WavTrackSet.FromParts(mix.Parts), Path.Combine(outDir, "mix.flac"), CancellationToken.None);
        var peaksFile = await PeakBuilder.BuildAsync(Path.Combine(outDir, "mix.flac"), CancellationToken.None);
        await PeakBuilder.WriteAsync(peaksFile, Path.Combine(outDir, "peaks.json"), CancellationToken.None);
        double decodedSeconds;
        using (var decoded = MediaFoundationDecoder.Open(Path.Combine(outDir, "mix.flac")))
        {
            long samples = 0;
            var buffer = new float[96_000];
            int n;
            while ((n = decoded.Read(buffer, 0, buffer.Length)) > 0)
            {
                samples += n;
            }

            decodedSeconds = samples / (double)decoded.Channels / decoded.SampleRate;
        }

        Console.WriteLine($"  mix: {mix.Frames:N0} frames = {mix.Duration.TotalSeconds:0.000} s [{mix.Format}], peak before limiter {mix.PeakBeforeLimiter:0.000}, limited samples {mix.LimitedSamples}");
        Console.WriteLine($"  mix.flac {mixFlac.Bytes:N0} B; decodes (plays) to {decodedSeconds:0.000} s; peaks.json {peaksFile.Peaks.Count} windows of {peaksFile.WindowMs} ms ({new FileInfo(Path.Combine(outDir, "peaks.json")).Length:N0} B)");

        DeleteMicrophoneAudio(outDir, tracksDir, result.Tracks.Where(t => t.Kind == AudioSourceKind.Microphone).Select(t => t.FileStem));
        return 0;
    }

    public static async Task<int> KillTestAsync(string outDir, int afterSeconds)
    {
        var tracksDir = Path.Combine(outDir, "tracks");
        Directory.CreateDirectory(tracksDir);
        Console.WriteLine($"output: {outDir}");
        using var player = TonePlayer.Start(outDir, afterSeconds + 30);
        var (mic, system, app) = await ResolveSourcesAsync(player.ProcessId);
        var psi = new ProcessStartInfo(Environment.ProcessPath!, $"kill-child --out \"{outDir}\" --mic \"{mic.Id}\" --system \"{system.Id}\" --app {app.ProcessId} --checkpoint 10")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using var child = Process.Start(psi)!;
        var ready = new TaskCompletionSource();
        var checkpoints = 0;
        var clock = new Stopwatch();
        child.OutputDataReceived += (_, e) =>
        {
            if (e.Data is null)
            {
                return;
            }

            Console.WriteLine($"  child: {e.Data}");
            if (e.Data.StartsWith("READY", StringComparison.Ordinal))
            {
                clock.Start();
                ready.TrySetResult();
            }
            else if (e.Data.StartsWith("CHECKPOINT", StringComparison.Ordinal))
            {
                Interlocked.Increment(ref checkpoints);
            }
        };
        child.BeginOutputReadLine();
        await ready.Task.WaitAsync(TimeSpan.FromSeconds(30));
        while (clock.Elapsed < TimeSpan.FromSeconds(afterSeconds) || Volatile.Read(ref checkpoints) == 0)
        {
            await Task.Delay(20);
        }

        child.Kill(); // TerminateProcess: no finally blocks, no flush, no header patch — like a crash.
        var killedAt = clock.Elapsed;
        await child.WaitForExitAsync();
        Console.WriteLine($"killed the recorder {killedAt.TotalSeconds:0.000} s after it started ({checkpoints} checkpoint(s) done), exit code 0x{child.ExitCode:X8}");

        foreach (var wav in Directory.GetFiles(tracksDir, "*.wav").Order(StringComparer.Ordinal))
        {
            var before = WavFileInfo.Read(wav);
            var repaired = StreamingWavWriter.Repair(wav);
            Console.WriteLine(
                $"  {Path.GetFileName(wav),-18} header said {before.Format.DurationOf(before.DeclaredDataBytes / before.Format.BlockAlign).TotalSeconds:0.000} s; repaired to {repaired.Duration.TotalSeconds:0.000} s "
                + $"({repaired.Frames:N0} frames, {repaired.TruncatedBytes} partial-frame bytes cut); lost vs kill time {(killedAt - repaired.Duration).TotalMilliseconds:0} ms");
        }

        DeleteMicrophoneAudio(outDir, tracksDir, ["mic"]);
        return 0;
    }

    public static async Task<int> KillChildAsync(string outDir, string micId, string systemId, int appPid, int checkpointSeconds)
    {
        var tracksDir = Path.Combine(outDir, "tracks");
        var session = await AudioRecordingSession.StartAsync(
            new AudioRecordingOptions(tracksDir, [micId, systemId, AudioSourceId.Application(appPid).ToString()]) { CheckpointInterval = TimeSpan.FromSeconds(checkpointSeconds) },
            CancellationToken.None);
        session.Checkpointed += (_, e) =>
        {
            Console.WriteLine($"CHECKPOINT {e.Checkpoint.Elapsed.TotalSeconds:0.000} s: " + string.Join("; ", e.Checkpoint.Tracks.Select(t => $"{t.FileStem} {t.Duration.TotalSeconds:0.000} s")));
            Console.Out.Flush();
        };
        Console.WriteLine("READY " + string.Join(", ", session.Tracks.Select(t => t.FileStem)));
        Console.Out.Flush();
        await Task.Delay(Timeout.Infinite);
        return 0;
    }

    private static async Task<(AudioSourceInfo Mic, AudioSourceInfo System, AudioSourceInfo App)> ResolveSourcesAsync(int playerPid)
    {
        var enumerator = new AudioSourceEnumerator();
        for (var attempt = 0; attempt < 50; attempt++)
        {
            var sources = enumerator.List();
            var app = sources.FirstOrDefault(s => s.ProcessId == playerPid);
            if (app is not null)
            {
                var mic = sources.First(s => s.Kind == AudioSourceKind.Microphone && s.IsDefault);
                var system = sources.First(s => s.Kind == AudioSourceKind.System && s.IsDefault);
                return (mic, system, app);
            }

            await Task.Delay(100);
        }

        throw new InvalidOperationException($"The tone player (pid {playerPid}) never opened an audio session.");
    }

    /// <summary>Amplitude of the 440 Hz component of channel 0 (Goertzel), in dBFS.</summary>
    private static double ToneDbfs(WavTrackSet track)
    {
        using var audio = MediaFoundationDecoder.Open(track);
        var channels = audio.Channels;
        var w = 2 * Math.PI * 440 / audio.SampleRate;
        var coefficient = 2 * Math.Cos(w);
        double s1 = 0, s2 = 0;
        long count = 0;
        var buffer = new float[audio.SampleRate * channels];
        int n;
        while ((n = audio.Read(buffer, 0, buffer.Length)) > 0)
        {
            for (var i = 0; i < n; i += channels)
            {
                var s = buffer[i] + (coefficient * s1) - s2;
                s2 = s1;
                s1 = s;
                count++;
            }
        }

        var power = (s1 * s1) + (s2 * s2) - (coefficient * s1 * s2);
        var amplitude = 2 * Math.Sqrt(Math.Max(0, power)) / Math.Max(1, count);
        return 20 * Math.Log10(amplitude + 1e-12);
    }

    /// <summary>Room audio never stays on disk: deletes the microphone tracks and the mix that contains them.</summary>
    private static void DeleteMicrophoneAudio(string outDir, string tracksDir, IEnumerable<string> micStems)
    {
        var deleted = new List<string>();
        foreach (var stem in micStems)
        {
            foreach (var file in Directory.GetFiles(tracksDir, stem + ".*"))
            {
                File.Delete(file);
                deleted.Add(Path.GetFileName(file));
            }
        }

        foreach (var file in Directory.GetFiles(outDir, "mix*.*").Concat(Directory.GetFiles(outDir, "peaks.json")))
        {
            File.Delete(file);
            deleted.Add(Path.GetFileName(file));
        }

        Console.WriteLine($"deleted microphone audio and everything derived from it: {string.Join(", ", deleted)}");
    }

    private static string Ppm(double? ppm) => ppm is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v:+0.0;-0.0} ppm") : "n/a";
}
