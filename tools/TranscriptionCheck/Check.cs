using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
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
