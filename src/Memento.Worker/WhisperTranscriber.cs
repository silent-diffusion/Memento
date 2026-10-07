using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Workers;
using Memento.Transcription;
using Memento.Transcription.Audio;
using Memento.Transcription.Windows;
using Memento.Transcription.Words;
using Whisper.net;
using Whisper.net.LibraryLoader;
using Whisper.net.Logger;

namespace Memento.Worker;

/// <summary>
/// The transcription job (ENGINE-NOTES.md §D): a speech-energy pass over every track first (silent tracks are skipped
/// and windows without speech are not sent to the engine), then each track in overlapping windows through Whisper.net
/// with runtime order Vulkan, CPU (never CUDA), the discrete GPU chosen by name, token timestamps and probabilities on,
/// a short punctuated prompt, and DTW off. Each window's kept segments are sent as soon as it finishes.
/// </summary>
internal sealed partial class WhisperTranscriber(ProtocolWriter output)
{
    private const int GpuThreads = 4;
    private readonly List<string> _nativeLog = [];

    public async Task<TranscribeResult> RunAsync(TranscribeJob job, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        if (!File.Exists(job.ModelPath))
        {
            throw new WorkerFailure(WorkerErrorCodes.ModelLoad, $"the model file {Path.GetFileName(job.ModelPath)} is missing");
        }

        // Pass 1: speech energy, silent tracks, windows.
        var plans = new List<TrackPlan>();
        foreach (var track in job.Tracks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var energy = new SpeechEnergy(TrackAudio.SampleRate);
            using (var audio = TrackAudio.Open(track.Path))
            {
                var buffer = new float[TrackAudio.SampleRate * 10];
                int n;
                while ((n = audio.Read(buffer, 0, buffer.Length)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    energy.Add(buffer.AsSpan(0, n));
                }
            }

            var regions = energy.Regions();
            var silent = SpeechEnergy.IsSilent(regions);
            var windows = WindowPlanner.Plan(energy.DurationSeconds, job.WindowSeconds, job.OverlapSeconds);
            var plan = new TrackPlan(track, energy.DurationSeconds, silent, regions, windows);
            plans.Add(plan);
            output.Send(new WorkerReply
            {
                Type = WorkerMessageTypes.Track,
                Track = new WorkerTrackInfo(
                    track.Id,
                    Math.Round(energy.DurationSeconds, 3),
                    silent,
                    Math.Round(energy.Rms, 5),
                    regions.Select(r => new[] { Math.Round(r.Start + track.OffsetSeconds, 2), Math.Round(r.End + track.OffsetSeconds, 2) }).ToList(),
                    windows.Count),
            });
        }

        var work = plans.Where(p => !p.Silent).Sum(p => p.Windows.Skip(p.Track.StartWindow).Sum(w => w.End - w.Start));
        var audioSeconds = plans.Where(p => !p.Silent).Sum(p => p.Duration);
        if (work <= 0)
        {
            return new TranscribeResult(job.Language == "auto" ? "en" : job.Language, job.Language == "auto", new WorkerDevice("none", "CPU", null, null, EngineVersion), audioSeconds, stopwatch.ElapsedMilliseconds);
        }

        // Pass 2: the engine.
        using var logging = LogProvider.AddLogger(OnNativeLog);
        var (factory, device) = LoadFactory(job);
        using var factoryScope = factory;
        output.Send(new WorkerReply { Type = WorkerMessageTypes.Device, Device = device });
        var builder = factory.CreateBuilder()
            .WithThreads(device.Runtime == TranscriptionDefaults.RuntimeCpu ? Math.Max(1, job.Threads) : GpuThreads)
            .WithTokenTimestamps()
            .WithProbabilities()
            .WithPrompt(job.Prompt)
            .WithLanguage(string.IsNullOrWhiteSpace(job.Language) ? "auto" : job.Language);
        await using var processor = builder.Build();

        string? language = job.Language == "auto" ? null : job.Language;
        double done = 0;
        foreach (var plan in plans.Where(p => !p.Silent))
        {
            TranscriptWord? lastWord = null;
            using var audio = TrackAudio.Open(plan.Track.Path);
            var reader = new WindowReader(audio);
            for (var i = plan.Track.StartWindow; i < plan.Windows.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var window = plan.Windows[i];
                var samples = reader.Read(window.Start, window.End);
                var segments = new List<WorkerSegment>();
                if (SpeechEnergy.HasSpeech(plan.Regions, window.Start, window.End))
                {
                    var offset = plan.Track.OffsetSeconds + window.Start;
                    var raw = new List<WorkerSegment>();
                    try
                    {
                        await foreach (var segment in processor.ProcessAsync(samples, cancellationToken))
                        {
                            language ??= string.IsNullOrWhiteSpace(segment.Language) ? null : segment.Language;
                            raw.Add(WordBuilder.ToSegment(ToRaw(segment), offset, keepWords: true));
                        }
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException and not WorkerFailure)
                    {
                        throw new WorkerFailure(WorkerErrorCodes.Engine, $"the engine failed on {plan.Track.Id} at {Clock(window.Start)} ({ex.Message.TrimEnd('.')})", ex);
                    }

                    var kept = SeamJoiner.Keep(raw, plan.Track.OffsetSeconds + window.KeepFrom, plan.Track.OffsetSeconds + window.KeepTo, lastWord);
                    if (kept.LastOrDefault(s => s.Words.Count > 0) is { } last)
                    {
                        lastWord = last.Words[^1];
                    }

                    segments.AddRange(kept.Where(s => s.Text.Length > 0).Select(s => job.WordTimestamps ? s : s with { Words = [] }));
                }

                done += window.End - window.Start;
                output.Send(new WorkerReply
                {
                    Type = WorkerMessageTypes.Progress,
                    Percent = Math.Round(Math.Min(100, 100 * done / work), 1),
                    TrackId = plan.Track.Id,
                    WindowsDone = i + 1,
                    Segments = segments,
                    Language = language,
                });
            }
        }

        return new TranscribeResult(language ?? "en", job.Language == "auto", device, audioSeconds, stopwatch.ElapsedMilliseconds);
    }

    private static string EngineVersion =>
        typeof(WhisperFactory).Assembly.GetName().Version is { } v ? string.Create(CultureInfo.InvariantCulture, $"{v.Major}.{v.Minor}.{v.Build}") : "unknown";

    private static string Clock(double seconds) => TimeSpan.FromSeconds(seconds).ToString(seconds >= 3600 ? @"h\:mm\:ss" : @"m\:ss", CultureInfo.InvariantCulture);

    private static RawSegment ToRaw(SegmentData segment) =>
        new(
            segment.Start.TotalSeconds,
            segment.End.TotalSeconds,
            segment.Text ?? string.Empty,
            segment.MinProbability,
            (segment.Tokens ?? []).Select(t => new TokenInfo(t.Text ?? string.Empty, t.Start / 100.0, t.End / 100.0, t.Probability)).ToList());

    /// <summary>
    /// Loads the model with the requested runtime order. On Vulkan the device whose name matches the host's discrete
    /// GPU is used: the native log lists the Vulkan devices when the backend starts, and if the first choice is not
    /// the right card the model is loaded again on the right one.
    /// </summary>
    private (WhisperFactory Factory, WorkerDevice Device) LoadFactory(TranscribeJob job)
    {
        var runtimes = job.Runtimes.Select(r => r switch
        {
            TranscriptionDefaults.RuntimeVulkan => RuntimeLibrary.Vulkan,
            TranscriptionDefaults.RuntimeCpu => RuntimeLibrary.Cpu,
            _ => throw new WorkerFailure(WorkerErrorCodes.InvalidJob, $"runtime '{r}' is not shipped with Memento"),
        }).ToList();
        RuntimeOptions.RuntimeLibraryOrder = runtimes;
        var wantsGpu = runtimes.Contains(RuntimeLibrary.Vulkan);
        var index = job.GpuDevice >= 0 ? job.GpuDevice : 0;
        var factory = Create(job.ModelPath, wantsGpu, index);
        var loaded = RuntimeOptions.LoadedLibrary;
        if (wantsGpu && loaded == RuntimeLibrary.Vulkan && job.GpuDevice < 0 && job.GpuName is { } gpuName)
        {
            var devices = VulkanDevices();
            var match = devices.FirstOrDefault(d => Matches(d.Name, gpuName));
            if (match.Name is not null && match.Index != index)
            {
                output.Log(string.Create(CultureInfo.InvariantCulture, $"Vulkan device {index} is not {gpuName}; loading the model on device {match.Index}."));
                factory.Dispose();
                index = match.Index;
                factory = Create(job.ModelPath, wantsGpu, index);
            }
        }

        var onGpu = RuntimeOptions.LoadedLibrary == RuntimeLibrary.Vulkan;
        var name = onGpu ? VulkanDevices().FirstOrDefault(d => d.Index == index).Name ?? job.GpuName : null;
        return (factory, new WorkerDevice(
            onGpu ? TranscriptionDefaults.RuntimeVulkan : TranscriptionDefaults.RuntimeCpu,
            onGpu ? "GPU (Vulkan)" : "CPU",
            name,
            onGpu ? index : null,
            EngineVersion));
    }

    private static WhisperFactory Create(string modelPath, bool useGpu, int device)
    {
        try
        {
            return WhisperFactory.FromPath(modelPath, new WhisperFactoryOptions { UseGpu = useGpu, GpuDevice = device, UseDtwTimeStamps = false });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new WorkerFailure(WorkerErrorCodes.ModelLoad, ex.Message, ex);
        }
    }

    private static bool Matches(string vulkanName, string dxgiName) =>
        vulkanName.Contains(dxgiName, StringComparison.OrdinalIgnoreCase) || dxgiName.Contains(vulkanName, StringComparison.OrdinalIgnoreCase);

    private List<(int Index, string Name)> VulkanDevices()
    {
        lock (_nativeLog)
        {
            return _nativeLog
                .Select(l => VulkanDeviceLine().Match(l))
                .Where(m => m.Success)
                .Select(m => (int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), m.Groups[2].Value.Trim()))
                .GroupBy(d => d.Item1)
                .Select(g => g.Last())
                .ToList();
        }
    }

    private void OnNativeLog(WhisperLogLevel level, string? message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        lock (_nativeLog)
        {
            if (_nativeLog.Count < 2000)
            {
                _nativeLog.Add(message);
            }
        }

        if (level <= WhisperLogLevel.Warning || message.Contains("vulkan", StringComparison.OrdinalIgnoreCase))
        {
            Console.Error.Write(message);
        }
    }

    [GeneratedRegex(@"ggml_vulkan: (\d+) = (.+?) \(", RegexOptions.CultureInvariant)]
    private static partial Regex VulkanDeviceLine();

    private sealed record TrackPlan(WorkerTrack Track, double Duration, bool Silent, IReadOnlyList<(double Start, double End)> Regions, IReadOnlyList<AudioWindow> Windows);
}
