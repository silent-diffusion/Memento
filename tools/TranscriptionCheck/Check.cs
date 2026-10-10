using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading.Channels;
using Memento.Core;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Models;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording.Simulation;
using Memento.Core.Settings;
using Memento.Core.Transcripts;
using Memento.Core.Workers;
using Memento.Transcription;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Tools.TranscriptionCheck;

/// <summary>The production registrations with the real probe, worker and model manager, minus the UI.</summary>
internal sealed class Check : IAsyncDisposable
{
    private readonly ServiceProvider _services;

    private Check(ServiceProvider services) => _services = services;

    private IModelManager Models => _services.GetRequiredService<IModelManager>();

    private ProcessingOrchestrator Processing => _services.GetRequiredService<ProcessingOrchestrator>();

    private IProjectStore Store => _services.GetRequiredService<IProjectStore>();

    private TranscriptStore Transcripts => _services.GetRequiredService<TranscriptStore>();

    public static async Task<Check> CreateAsync(string? workerPath)
    {
        var worker = workerPath ?? FindWorker();
        Console.WriteLine($"data root: {AppPaths.DataRoot}");
        Console.WriteLine($"worker:    {worker}");
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        var ui = new NoUi();
        services.AddSingleton<IAppInfo>(ui);
        services.AddSingleton<IThemeState>(ui);
        services.AddSingleton<IExternalLauncher>(ui);
        services.AddSingleton<IUiLifecycle>(ui);
        services.AddSingleton<IFolderPicker>(ui);
        services.AddSingleton<IBridgeEventSink>(new ConsoleSink());
        services.AddSingleton<IFreeSpaceProbe, DriveFreeSpaceProbe>();
        services.AddSingleton<ISettingsStore>(sp => new JsonSettingsStore(AppPaths.SettingsFile, sp.GetRequiredService<ILogger<JsonSettingsStore>>()));
        services.AddSingleton<IResourceProbe>(sp => new WindowsResourceProbe(sp.GetRequiredService<ILogger<WindowsResourceProbe>>()));
        services.AddSingleton(new WorkerLocation(worker));
        services.AddMementoBridge();
        services.AddMementoLibrary();
        services.AddSimulatedAudio(new SimulatedEngineOptions());
        services.AddMementoTranscription();
        var provider = services.BuildServiceProvider();
        await provider.GetRequiredService<ISettingsStore>().LoadAsync(CancellationToken.None);
        await provider.GetRequiredService<ILibraryIndex>().InitializeAsync(CancellationToken.None);
        return new Check(provider);
    }

    public async ValueTask DisposeAsync()
    {
        await Processing.StopAsync();
        await _services.DisposeAsync();
    }

    public void ListModels()
    {
        var probe = _services.GetRequiredService<IResourceProbe>().Sample();
        foreach (var gpu in probe.Gpus)
        {
            Console.WriteLine($"GPU {gpu.AdapterIndex}: {gpu.Name} vendor 0x{gpu.VendorId:X4} dedicated {gpu.DedicatedVideoMemoryBytes / 1048576} MiB free {(gpu.FreeVramBytes ?? 0) / 1048576} MiB discrete {gpu.IsDiscrete}");
        }

        Console.WriteLine($"recommended transcription model: {_services.GetRequiredService<EngineSelector>().RecommendedTranscriptionModelId(probe)}");
        foreach (var model in Models.List())
        {
            Console.WriteLine($"{model.Entry.Id,-28} {model.Entry.Kind,-14} {model.Entry.SizeBytes,14:N0} B  {(model.Installed ? "installed" : "-")}  {model.Entry.License}");
        }
    }

    public async Task<int> InstallAsync(IReadOnlyList<string> ids)
    {
        foreach (var id in ids)
        {
            var done = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnInstalled(object? sender, string installed)
            {
                if (installed == id)
                {
                    done.TrySetResult(true);
                }
            }

            Models.Installed += OnInstalled;
            var stopwatch = Stopwatch.StartNew();
            try
            {
                await Models.InstallAsync(id, CancellationToken.None);
                if (!Models.IsInstalled(id))
                {
                    // Poll for failure too: a failed download raises no Installed event.
                    while (!done.Task.IsCompleted && !Models.IsInstalled(id) && Models.List().First(m => m.Entry.Id == id).Installing is not null)
                    {
                        await Task.WhenAny(done.Task, Task.Delay(1000));
                    }
                }
            }
            catch (BridgeException ex)
            {
                Console.WriteLine($"{id}: {ex.Code}: {ex.Message} ({ex.Detail})");
                return 1;
            }
            finally
            {
                Models.Installed -= OnInstalled;
            }

            var path = Models.Resolve(id);
            if (path is null)
            {
                Console.WriteLine($"{id}: NOT installed after {stopwatch.Elapsed.TotalSeconds:0.0} s");
                return 1;
            }

            var size = new FileInfo(path).Length;
            Console.WriteLine($"{id}: installed in {stopwatch.Elapsed.TotalSeconds:0.0} s ({size / 1048576.0 / stopwatch.Elapsed.TotalSeconds:0.0} MiB/s), {size:N0} B");
            Console.WriteLine($"{id}: SHA-256 on disk {await Sha256Async(path)} (catalog {Models.Catalog.Find(id)!.Sha256})");
        }

        return 0;
    }

    public async Task<int> AdoptAsync(string id, string file)
    {
        var entry = Models.Catalog.Find(id) ?? throw new ArgumentException($"no model {id}");
        var hash = await Sha256Async(file);
        if (hash != entry.Sha256)
        {
            Console.WriteLine($"{id}: {file} has SHA-256 {hash}, the catalog says {entry.Sha256}; not adopted");
            return 1;
        }

        var target = ((ModelManager)Models).PathOf(entry);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(file, target, overwrite: true);
        Console.WriteLine($"{id}: verified {hash} and copied to {target}");
        return 0;
    }

    public async Task<int> RunAsync(string wav, string? modelId, bool cpu, string title)
    {
        await ConfigureAsync(modelId);
        var id = await ImportAsync(wav, title, new ProcessingRequest(ForceCpu: cpu));
        var stopwatch = Stopwatch.StartNew();
        await Processing.EnqueueAfterStoredAsync(id, CancellationToken.None);
        await Task.Delay(200);
        await Processing.WhenIdleAsync();
        Console.WriteLine($"pipeline (transcript + speakers + topics) took {stopwatch.Elapsed.TotalSeconds:0.0} s");
        return await ShowAsync(id);
    }

    public async Task<int> KillTestAsync(string wav, string? modelId)
    {
        await ConfigureAsync(modelId);
        var id = await ImportAsync(wav, "Kill test", new ProcessingRequest());
        await Processing.EnqueueAfterStoredAsync(id, CancellationToken.None);

        // Wait for the first finished window, then kill the worker like a native abort would.
        while (await Transcripts.LoadPartialAsync(id, CancellationToken.None) is not { Segments.Count: > 0 })
        {
            await Task.Delay(100);
        }

        foreach (var process in Process.GetProcessesByName("Memento.Worker"))
        {
            Console.WriteLine($"killing worker {process.Id}");
            process.Kill();
        }

        await Task.Delay(500);
        await Processing.WhenIdleAsync();
        var manifest = await Store.LoadAsync(id, CancellationToken.None);
        var failure = manifest.Failures.FirstOrDefault(f => f.Stage == StageNames.Transcript);
        Console.WriteLine($"transcript stage: {manifest.Stages.FirstOrDefault(s => s.Stage == StageNames.Transcript)}");
        Console.WriteLine($"failure: {failure?.Message}");
        Console.WriteLine($"kept:    {failure?.Kept}");
        Console.WriteLine($"remedies: {string.Join(", ", failure?.Remedies.Select(r => $"{r.Id} ({r.Label})") ?? [])}");
        var partial = await Transcripts.LoadAsync(id, CancellationToken.None);
        Console.WriteLine($"transcript.json after the kill: {(partial is null ? "none" : $"{partial.Segments.Count} segments, complete {partial.Complete}, up to {partial.Segments.Max(s => s.End):0.0} s")}");
        if (failure is null)
        {
            return 1;
        }

        var remedy = failure.Remedies.Any(r => r.Id == Remedies.Cpu) ? Remedies.Cpu : failure.Remedies[0].Id;
        Console.WriteLine($"retrying with remedy {remedy}");
        var stopwatch = Stopwatch.StartNew();
        await Processing.RetryAsync(id, StageNames.Transcript, remedy, CancellationToken.None);
        await Task.Delay(200);
        await Processing.WhenIdleAsync();
        Console.WriteLine($"retry took {stopwatch.Elapsed.TotalSeconds:0.0} s");
        return await ShowAsync(id);
    }

    /// <summary>
    /// The live transcript's worker (2.0) on one WAV: the model is loaded once, then each 10-second window of the file (read
    /// and mixed by <see cref="LiveMixReader"/>, as the app reads a recording in progress) is sent at recording pace, one
    /// window every 10 s, or back to back with <paramref name="paced"/> false. Prints each window's time, the lines heard,
    /// and the worker's processor time per window and over the run (ENGINE-NOTES.md §O).
    /// </summary>
    public async Task<int> LiveAsync(string wav, string modelFile, int threads, int windows, bool paced, bool gpu)
    {
        var full = Path.GetFullPath(wav);
        var source = new LiveTrackSource("mix", Path.GetDirectoryName(full)!, Path.GetFileName(full), 0, null);
        var covered = LiveMixReader.CoveredUntilMs(source);
        var job = new LiveJob(Path.GetFullPath(modelFile), Path.GetFileNameWithoutExtension(modelFile), gpu ? [WorkerRuntimes.Vulkan, WorkerRuntimes.Cpu] : [WorkerRuntimes.Cpu], -1, null, "en", TranscriptionDefaults.Prompt, threads);
        var heard = Channel.CreateUnbounded<WorkerReply>();
        var workers = _services.GetRequiredService<WorkerClient>();
        var load = Stopwatch.StartNew();
        await using var session = await workers.OpenAsync(new WorkerJob(WorkerJobKinds.Live, Live: job), reply =>
        {
            heard.Writer.TryWrite(reply);
            return Task.CompletedTask;
        }, CancellationToken.None);
        WorkerReply device;
        do
        {
            device = await heard.Reader.ReadAsync();
        }
        while (device.Type != WorkerMessageTypes.Device);
        Console.WriteLine($"model {Path.GetFileName(modelFile)} loaded on {device.Device?.Device} in {load.Elapsed.TotalSeconds:0.0} s, {threads} threads");
        var cpuAtStart = workers.RunningCpuTime;
        var run = Stopwatch.StartNew();
        var busy = TimeSpan.Zero;
        var count = (int)Math.Min(windows, covered / LiveWindows.WindowMs);
        for (var w = 0; w < count; w++)
        {
            var mix = LiveMixReader.Read([source], w * LiveWindows.WindowMs, (w + 1) * LiveWindows.WindowMs);
            var cpuBefore = workers.RunningCpuTime;
            var window = Stopwatch.StartNew();
            await session.SendAsync(new WorkerCommand(WorkerMessageTypes.Audio, Audio: new LiveAudio(w, w * 10.0, LiveAudio.Encode(mix.Samples))));
            WorkerReply reply;
            do
            {
                reply = await heard.Reader.ReadAsync();
            }
            while (reply.Type != WorkerMessageTypes.Heard);
            busy += window.Elapsed;
            var cpu = workers.RunningCpuTime - cpuBefore;
            Console.WriteLine($"window {w}: {window.Elapsed.TotalSeconds:0.00} s, worker CPU {cpu.TotalSeconds:0.00} s, {reply.Segments?.Count ?? 0} lines: {string.Join(" | ", (reply.Segments ?? []).Select(s => s.Text))}");
            if (paced)
            {
                var next = TimeSpan.FromSeconds(10 * (w + 1)) - run.Elapsed;
                if (next > TimeSpan.Zero)
                {
                    await Task.Delay(next);
                }
            }
        }

        var total = workers.RunningCpuTime - cpuAtStart;
        Console.WriteLine($"{count} windows in {run.Elapsed.TotalSeconds:0.0} s; engine busy {busy.TotalSeconds:0.0} s ({100 * busy.TotalSeconds / Math.Max(1, count * 10.0):0}% of the audio time)");
        Console.WriteLine($"worker CPU {total.TotalSeconds:0.0} s over {run.Elapsed.TotalSeconds:0.0} s = {100 * total.TotalSeconds / run.Elapsed.TotalSeconds / Environment.ProcessorCount:0.0}% of all {Environment.ProcessorCount} processor threads");
        await session.SendAsync(new WorkerCommand(WorkerMessageTypes.End));
        await session.Completion;
        return 0;
    }

    /// <summary>The worker's speaker job on one audio file, with its turns and voices saved for offline comparison.</summary>
    public async Task<int> DiarizeAsync(string audio, string segmentation, string embedding, float threshold, int count, string output)
    {
        var job = new DiarizeJob([new WorkerTrack("track", Path.GetFullPath(audio), 0)], segmentation, embedding, count, threshold, TranscriptionDefaults.DiarizationThreads);
        var stopwatch = Stopwatch.StartNew();
        var reply = await _services.GetRequiredService<WorkerClient>().RunAsync(new WorkerJob(WorkerJobKinds.Diarize, Diarize: job), null, CancellationToken.None);
        var result = reply.Diarization!;
        var track = result.Tracks[0];
        var clusters = track.Turns.GroupBy(t => t.Speaker).Select(g => (Speaker: g.Key, Seconds: g.Sum(t => t.End - t.Start))).OrderByDescending(c => c.Seconds).ToList();
        var total = Math.Max(0.001, clusters.Sum(c => c.Seconds));
        Console.WriteLine($"threshold {threshold.ToString(CultureInfo.InvariantCulture)}, count {count}: {clusters.Count} speakers in {result.AudioSeconds:0} s of audio, {stopwatch.Elapsed.TotalSeconds:0.0} s");
        Console.WriteLine($"  with at least 2% of the speech: {clusters.Count(c => c.Seconds / total >= 0.02)}; under 10 s each: {clusters.Count(c => c.Seconds < 10)}");
        Console.WriteLine($"  shares: {string.Join(", ", clusters.Select(c => $"{100 * c.Seconds / total:0.0}%"))}");
        await File.WriteAllTextAsync(output, System.Text.Json.JsonSerializer.Serialize(reply, WorkerJsonContext.Default.WorkerReply));
        return 0;
    }

    /// <summary>
    /// Identifies the speakers of a recording already in the library again, through the real stage and worker, with the
    /// recording's own count (Who spoke; 0 sets it back to Auto) when one is given, and prints the result. Run it twice to see the second pass
    /// regroup from voices.json.
    /// </summary>
    public async Task<int> IdentifyAsync(string id, int? count)
    {
        await ConfigureAsync(null);
        if (count is not null)
        {
            await _services.GetRequiredService<ProjectService>().UpdateDetailsAsync(id, new RecordingDetailsPatch { WhoSpoke = new WhoSpoke(count == 0 ? null : count, []) }, CancellationToken.None);
        }

        var stopwatch = Stopwatch.StartNew();
        await Processing.RetryAsync(id, StageNames.Speakers, Remedies.Retry, CancellationToken.None);
        await Task.Delay(200);
        await Processing.WhenIdleAsync();
        Console.WriteLine($"speakers identified again in {stopwatch.Elapsed.TotalSeconds:0.0} s");
        var transcript = await Transcripts.LoadAsync(id, CancellationToken.None);
        var total = Math.Max(1, transcript!.Speakers.Sum(s => s.TalkTimeMs));
        Console.WriteLine($"{transcript.Speakers.Count} speakers: {string.Join(", ", transcript.Speakers.Select(s => $"{s.Id} {100.0 * s.TalkTimeMs / total:0.0}%{(s.Renamed ? " (named)" : string.Empty)}"))}");
        foreach (var entry in (await Store.ReadHistoryAsync(id, CancellationToken.None)).Where(h => h.Stage == StageNames.Speakers).TakeLast(2))
        {
            Console.WriteLine($"  {entry.Stage}/{entry.Event}: {entry.Summary} | {entry.Detail}");
        }

        return 0;
    }

    public async Task<int> ShowAsync(string id)
    {
        var manifest = await Store.LoadAsync(id, CancellationToken.None);
        var transcript = await Transcripts.LoadAsync(id, CancellationToken.None);
        Console.WriteLine($"recording {id}: {string.Join(", ", manifest.Stages.Select(s => $"{s.Stage}={s.State}"))}");
        foreach (var failure in manifest.Failures)
        {
            Console.WriteLine($"  failure {failure.Stage}: {failure.Message} | {failure.Kept} | {string.Join(", ", failure.Remedies.Select(r => r.Id))}");
        }

        if (transcript is null)
        {
            Console.WriteLine("no transcript");
            return 1;
        }

        var audio = manifest.DurationMs / 1000.0;
        var words = transcript.Segments.SelectMany(s => s.Words).ToList();
        var low = words.Count(w => w.C < 0.5);
        Console.WriteLine($"engine: {transcript.Engine.Name} {transcript.Engine.Version} {transcript.Engine.Model} on {transcript.Engine.Device}, pass {transcript.Engine.DurationMs / 1000.0:0.0} s for {audio:0.0} s of audio => RTF {transcript.Engine.DurationMs / 1000.0 / audio:0.000}");
        Console.WriteLine($"language {transcript.Language} (detected {transcript.LanguageDetected}); {transcript.Segments.Count} segments, {words.Count} words, {low} below 0.5 confidence ({100.0 * low / Math.Max(1, words.Count):0.00}%), complete {transcript.Complete}, version {transcript.Version}");
        Console.WriteLine($"coverage gaps: {(transcript.CoverageGaps.Count == 0 ? "none" : string.Join(", ", transcript.CoverageGaps.Select(g => $"{g.Start:0.0}-{g.End:0.0}")))}");
        var total = Math.Max(1, transcript.Speakers.Sum(s => s.TalkTimeMs));
        Console.WriteLine($"speakers: {transcript.Speakers.Count}: {string.Join(", ", transcript.Speakers.Select(s => $"{s.Id} '{s.Name}' color {s.Color} {s.TalkTimeMs / 1000.0:0.0} s ({100.0 * s.TalkTimeMs / total:0.0}%)"))}");
        var uncertain = transcript.Segments.Count(s => s.SpeakerConfidence is < 0.7);
        Console.WriteLine($"segments with uncertain speaker (<0.7): {uncertain}");
        var annotations = await Store.LoadAnnotationsAsync(id, CancellationToken.None);
        Console.WriteLine($"topics: {string.Join(", ", annotations.Topics.Select(t => $"{t.Label} ({t.Origin})"))}");
        Console.WriteLine("first lines:");
        foreach (var segment in transcript.Segments.Take(4))
        {
            Console.WriteLine($"  [{segment.Start,7:0.00}-{segment.End,7:0.00}] {segment.Speaker ?? "-",-5} c={segment.Confidence:0.00} {segment.Text}");
        }

        Console.WriteLine("history:");
        foreach (var entry in await Store.ReadHistoryAsync(id, CancellationToken.None))
        {
            Console.WriteLine($"  {entry.Stage}/{entry.Event}: {entry.Summary} | {entry.Detail}");
        }

        return 0;
    }

    private static string FindWorker()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Memento.sln")))
            {
                return Path.Combine(directory.FullName, "src", "Memento.Worker", "bin", "Release", "net8.0-windows", "win-x64", WorkerLocation.ExecutableName);
            }
        }

        return WorkerLocation.Default.ExecutablePath;
    }

    private static async Task<string> Sha256Async(string path)
    {
        await using var stream = File.OpenRead(path);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream)).ToLowerInvariant();
    }

    private async Task ConfigureAsync(string? modelId) =>
        await _services.GetRequiredService<ISettingsStore>().UpdateAsync(
            s => s with
            {
                Transcription = s.Transcription with { Auto = true, ModelId = modelId ?? s.Transcription.ModelId },
                Speakers = s.Speakers with { Identify = true },
            },
            CancellationToken.None);

    /// <summary>A stored recording whose only track (and mix) is the WAV, as if it had just been finalized.</summary>
    private async Task<string> ImportAsync(string wav, string title, ProcessingRequest request)
    {
        var created = await Store.CreateAsync(new ProjectCreateRequest(title, "meeting", DateTimeOffset.Now, ProjectStates.Finalizing), CancellationToken.None);
        var folder = Store.GetProjectFolder(created.Id);
        var track = Path.Combine(folder, "tracks", "mic.wav");
        File.Copy(wav, track);
        File.Copy(wav, Path.Combine(folder, "mix.wav"));
        PcmFormat format;
        long frames;
        using (var reader = new WavReader(track))
        {
            format = reader.Format;
            frames = reader.TotalFrames;
        }

        var durationMs = frames * 1000 / format.SampleRate;
        var hash = await Sha256Async(track);
        await _services.GetRequiredService<ProjectCatalog>().UpdateAsync(
            created.Id,
            m => m with
            {
                State = ProjectStates.Ready,
                DurationMs = durationMs,
                Tracks =
                [
                    new ProjectTrack
                    {
                        Id = "mic",
                        SourceId = "mic:file",
                        SourceKind = "microphone",
                        Name = "Imported audio",
                        File = "tracks/mic.wav",
                        SampleRate = format.SampleRate,
                        Channels = format.Channels,
                        BitsPerSample = format.BitsPerSample,
                        Codec = "wav",
                        DurationMs = durationMs,
                        Sha256 = hash,
                    },
                ],
                Mix = new ProjectMix("mix.wav", "wav", format.SampleRate, format.Channels, durationMs, hash),
                Stages = [new StageStatus(StageNames.Stored, StageStates.Done, null, "Done")],
                Processing = request,
            },
            CancellationToken.None);
        Console.WriteLine($"imported {Path.GetFileName(wav)} as {created.Id} ({durationMs / 1000.0:0.0} s, {format.SampleRate} Hz, {format.Channels} ch)");
        return created.Id;
    }
}
