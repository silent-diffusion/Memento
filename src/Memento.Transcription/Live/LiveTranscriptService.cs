using System.Globalization;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Host;
using Memento.Core.Models;
using Memento.Core.Processing;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Memento.Core.Settings;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging;

namespace Memento.Transcription.Live;

/// <summary>
/// The live transcript while recording (2.0; Settings › Transcription › Live transcript while recording, off by
/// default). While a session records, every 10-second window of the mix of its tracks (<see cref="LiveMixReader"/>, read
/// behind the writers from the capture files on disk, never from the capture threads) goes to one worker that keeps the
/// Small model loaded (Base when Small is not installed; never a larger one), on the processor unless Settings allows
/// the graphics card and the card is free. Each window's lines are appended and sent as <c>recording.liveTranscript</c>,
/// marked provisional by the page; they are kept in memory only, never written to the project, and the full pass after
/// Stop replaces them.
/// It never competes with a full pass: while a heavy stage runs it stops its worker and waits; on the card it lets go as
/// soon as another job asks for the card (<see cref="WorkerClient.GpuWanted"/>) and carries on from the processor; it
/// never queues behind a job on the card (<see cref="WorkerClient.TryOpenAsync"/>). A window it falls behind on is
/// skipped (<see cref="LiveWindows"/>). Its worker runs at below-normal priority with few threads.
/// </summary>
public sealed partial class LiveTranscriptService : IAsyncDisposable, IDisposable
{
    public const string SmallModelId = "whisper-small";
    public const string BaseModelId = "whisper-base";

    /// <summary>How often the loop looks at the session (the window check is cheap: file lengths).</summary>
    public static readonly TimeSpan TickInterval = TimeSpan.FromMilliseconds(500);

    /// <summary>The newest lines sent in each event (the card shows four; the rest is history the page does not need).</summary>
    public const int MaxLinesSent = 100;

    /// <summary>How long one window may take before the worker counts as stuck and is restarted.</summary>
    public static readonly TimeSpan WindowLimit = TimeSpan.FromMinutes(2);

    internal const string PausedRecording = "Paused with the recording. Words carry on when you resume.";
    internal const string PausedForFullPass = "Waiting while Memento transcribes another recording, so the live draft never slows a full transcript. It carries on when that is done.";
    internal const string NoModel = "The live transcript needs the Small or Base transcription model. Install one in Settings › Transcription; the full transcript is still made after you stop.";
    internal const string TurnedOff = "Live transcript was turned off in Settings › Transcription. The full transcript is made after you stop.";

    private readonly RecordingCoordinator _recordings;
    private readonly IProjectStore _store;
    private readonly ISettingsStore _settings;
    private readonly IModelManager _models;
    private readonly EngineSelector _selector;
    private readonly WorkerClient _workers;
    private readonly ProcessingOrchestrator _processing;
    private readonly IBridgeEventSink _sink;
    private readonly ILogger<LiveTranscriptService> _logger;
    private readonly SemaphoreSlim _tick = new(1, 1);
    private readonly List<LiveTranscriptSegment> _lines = [];
    private CancellationTokenSource? _loop;
    private Task _running = Task.CompletedTask;
    private Session? _session;
    private int _disposed;

    public LiveTranscriptService(
        RecordingCoordinator recordings,
        IProjectStore store,
        ISettingsStore settings,
        IModelManager models,
        EngineSelector selector,
        WorkerClient workers,
        ProcessingOrchestrator processing,
        IBridgeEventSink sink,
        ILogger<LiveTranscriptService> logger)
    {
        _recordings = recordings;
        _store = store;
        _settings = settings;
        _models = models;
        _selector = selector;
        _workers = workers;
        _processing = processing;
        _sink = sink;
        _logger = logger;
        _workers.GpuWanted += OnGpuWanted;
    }

    /// <summary>The lines of the session in progress (tests).</summary>
    public IReadOnlyList<LiveTranscriptSegment> Lines
    {
        get
        {
            lock (_lines)
            {
                return [.. _lines];
            }
        }
    }

    /// <summary>The worker of the session in progress runs on the graphics card (tests).</summary>
    public bool OnGpu => _session?.Worker is { OnGpu: true };

    /// <summary>Starts the loop (the app's hosted lifetime); tests call <see cref="TickAsync"/> instead.</summary>
    public void Start()
    {
        _loop = new CancellationTokenSource();
        var token = _loop.Token;
        _running = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TickInterval);
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    await TickAsync(token);
                }
            }
            catch (OperationCanceledException)
            {
                // Closing.
            }
        }, CancellationToken.None);
    }

    /// <summary>
    /// One step: follows the session in progress, starts or stops the worker, and hears the next complete window.
    /// Never throws for a worker or file problem (the card says it, recording is not affected).
    /// </summary>
    public async Task TickAsync(CancellationToken cancellationToken)
    {
        await _tick.WaitAsync(cancellationToken);
        try
        {
            await StepAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
#pragma warning disable CA1031 // The live draft is best effort; recording and the full pass never depend on it.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogStepFailed(ex);
            if (_session is { } session)
            {
                await StopWorkerAsync(session);
                Publish(session, LiveTranscriptStates.Failed, $"The live transcript stopped ({Reason(ex)}). Recording is not affected, and the full transcript is made after you stop.");
                session.Failed = true;
            }
        }
        finally
        {
            _tick.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _workers.GpuWanted -= OnGpuWanted;
        if (_loop is { } loop)
        {
            await loop.CancelAsync();
            await _running.ConfigureAwait(false);
            loop.Dispose();
        }

        if (Interlocked.Exchange(ref _session, null) is { } session)
        {
            await StopWorkerAsync(session);
        }
    }

    /// <summary>For a container that disposes synchronously; the same as <see cref="DisposeAsync"/>.</summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    private static string Reason(Exception ex) => ex.Message.TrimEnd('.');

    private async Task StepAsync(CancellationToken cancellationToken)
    {
        var current = _recordings.Current;
        var live = current is { State: "recording" or "paused" };
        var wanted = _settings.Current.Transcription.LiveDuringRecording;
        if (!live)
        {
            await EndSessionAsync();
            return;
        }

        if (_session is { } previous && previous.SessionId != current!.SessionId)
        {
            await EndSessionAsync();
        }

        if (!wanted)
        {
            if (_session is { } running)
            {
                // Turned off while recording: the worker goes, the lines stay on the card until the session ends.
                await StopWorkerAsync(running);
                Publish(running, LiveTranscriptStates.Paused, TurnedOff);
                running.Off = true;
            }

            return;
        }

        var session = _session ??= StartSession(current!);
        if (session.Off)
        {
            session.Off = false;
        }

        if (session.Failed)
        {
            return;
        }

        if (current!.State == "paused")
        {
            Publish(session, LiveTranscriptStates.Paused, PausedRecording);
            return;
        }

        if (_processing.IsHeavyStageRunning)
        {
            await StopWorkerAsync(session);
            Publish(session, LiveTranscriptStates.Paused, PausedForFullPass);
            return;
        }

        if (session.Worker is { } gone && (gone.LettingGo || gone.Session?.Completion.IsCompleted == true))
        {
            // It let go of the graphics card, or ended: another one starts (on the processor while the card is taken).
            await StopWorkerAsync(session);
        }

        if (session.Worker is null && !await StartWorkerAsync(session, cancellationToken))
        {
            return;
        }

        var sources = current.Tracks
            .Select(t => new LiveTrackSource(t.Id, session.Folder, CaptureFile(t.File), t.StartOffsetMs, t.EndedEarlyAtMs))
            .ToList();
        var covered = LiveWindows.Covered(sources.Select(s => (Coverage(s), s.EndedAtMs is null)), current.ElapsedMs);
        if (LiveWindows.Next(session.Next, covered) is not { } window)
        {
            Publish(session, LiveTranscriptStates.Listening, null);
            return;
        }

        session.Next = window.Index + 1;
        if (window.Skipped > 0)
        {
            LogSkipped(window.Skipped, window.Index);
        }

        var mix = await Task.Run(() => LiveMixReader.Read(sources, window.StartMs, window.EndMs), cancellationToken);
        if (mix.IsSilent)
        {
            Publish(session, LiveTranscriptStates.Listening, null);
            return;
        }

        var heard = await HearAsync(session, window, mix, cancellationToken);
        if (heard is null)
        {
            return;
        }

        lock (_lines)
        {
            _lines.AddRange(heard.Select(s => new LiveTranscriptSegment(s.Start, s.End, s.Text)));
        }

        Publish(session, LiveTranscriptStates.Listening, null, force: true);
    }

    /// <summary>The capture file of a track: the bridge's <c>Track.file</c> names the WAV while recording.</summary>
    private static string CaptureFile(string file) => file.Replace('\\', '/');

    private static long Coverage(LiveTrackSource source)
    {
        try
        {
            return LiveMixReader.CoveredUntilMs(source);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            // Not written yet (a track just added), or held for a moment: it holds the window back until it is readable.
            return source.StartOffsetMs;
        }
    }

    private Session StartSession(RecordingStatePayload current)
    {
        lock (_lines)
        {
            _lines.Clear();
        }

        var session = new Session(current.SessionId, current.RecordingId, _store.GetProjectFolder(current.RecordingId));
        LogSessionStarted(current.SessionId);
        Publish(session, LiveTranscriptStates.Starting, null);
        return session;
    }

    private async Task EndSessionAsync()
    {
        if (_session is not { } session)
        {
            return;
        }

        _session = null;
        lock (_lines)
        {
            // Provisional only: nothing of it is kept once the session is over (the full pass replaces it).
            _lines.Clear();
        }

        LogSessionEnded(session.SessionId, session.Windows);
        await StopWorkerAsync(session);
    }

    private async Task<bool> StartWorkerAsync(Session session, CancellationToken cancellationToken)
    {
        var modelId = await ChooseModelAsync(cancellationToken);
        if (modelId is null || _models.Resolve(modelId) is not { } path)
        {
            Publish(session, LiveTranscriptStates.Unavailable, NoModel);
            return false;
        }

        var transcription = _settings.Current.Transcription;
        var device = transcription.LiveOnGpu ? _selector.SelectDevice(modelId, forceCpu: false) : null;
        var lease = _models.Use(modelId);
        var worker = new LiveWorker(modelId, _models.Catalog.Find(modelId)?.Name ?? modelId, lease);
        WorkerSession? opened = null;
        if (device is { UseGpu: true } && !session.GaveUpGpu && !_workers.IsGpuBusy)
        {
            opened = await _workers.TryOpenAsync(Job(path, modelId, device, transcription.Language), worker.OnReplyAsync, cancellationToken);
            worker.OnGpu = opened is not null;
        }

        opened ??= await _workers.OpenAsync(Job(path, modelId, null, transcription.Language), worker.OnReplyAsync, cancellationToken);
        worker.Session = opened;
        session.Worker = worker;
        LogWorkerStarted(modelId, worker.OnGpu);
        return true;
    }

    private static WorkerJob Job(string path, string modelId, EngineDevice? device, string language) =>
        new(
            WorkerJobKinds.Live,
            Live: new LiveJob(
                path,
                modelId,
                device is { UseGpu: true } ? [TranscriptionDefaults.RuntimeVulkan, TranscriptionDefaults.RuntimeCpu] : [TranscriptionDefaults.RuntimeCpu],
                -1,
                device?.Gpu?.Name,
                language,
                TranscriptionDefaults.Prompt,
                device is { UseGpu: true } ? 2 : LiveThreads));

    /// <summary>A quarter of the processor threads, 1 to 4: the draft never takes the processor from recording or the PC.</summary>
    internal static int LiveThreads => Math.Clamp(Environment.ProcessorCount / 4, 1, 4);

    /// <summary>Small when installed and intact, else Base; never Turbo, Medium or Large (too heavy beside a recording).</summary>
    private async Task<string?> ChooseModelAsync(CancellationToken cancellationToken)
    {
        foreach (var id in new[] { SmallModelId, BaseModelId })
        {
            if (_models.IsInstalled(id) && await _models.VerifyAsync(id, cancellationToken) != ModelCheck.Damaged)
            {
                return id;
            }
        }

        return null;
    }

    private async Task<IReadOnlyList<WorkerSegment>?> HearAsync(Session session, LiveWindow window, LiveMix mix, CancellationToken cancellationToken)
    {
        var worker = session.Worker!;
        var reply = worker.Expect(window.Index);
        try
        {
            await worker.Session!.SendAsync(new WorkerCommand(WorkerMessageTypes.Audio, Audio: new LiveAudio(window.Index, window.StartMs / 1000.0, LiveAudio.Encode(mix.Samples))));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // The worker went away (it let go of the card, or crashed): the next step starts another.
            await StopWorkerAsync(session);
            return null;
        }

        var finished = await Task.WhenAny(reply, worker.Session.Completion, Task.Delay(WindowLimit, cancellationToken));
        if (finished == reply)
        {
            session.Windows++;
            worker.Device ??= worker.OnGpu ? "GPU" : "CPU";
            return await reply;
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (finished == worker.Session.Completion && worker.LettingGo)
        {
            // It let go of the graphics card for a full pass or a document: the next step goes on from the processor.
            await StopWorkerAsync(session);
            return null;
        }

        var why = finished == worker.Session.Completion
            ? await DescribeEndAsync(worker.Session.Completion)
            : string.Create(CultureInfo.InvariantCulture, $"a window took longer than {WindowLimit.TotalMinutes:0} minutes");
        await StopWorkerAsync(session);
        session.Restarts++;
        if (session.Restarts > 1)
        {
            throw new IOException(why);
        }

        LogWorkerRestarted(why);
        return null;
    }

    private static async Task<string> DescribeEndAsync(Task<WorkerReply> completion)
    {
        try
        {
            await completion;
            return "its worker ended";
        }
        catch (WorkerJobException ex)
        {
            return ex.Message.TrimEnd('.');
        }
        catch (WorkerCrashedException)
        {
            return "its worker stopped unexpectedly";
        }
        catch (OperationCanceledException)
        {
            return "its worker was stopped";
        }
    }

    private static async Task StopWorkerAsync(Session session)
    {
        if (session.Worker is not { } worker)
        {
            return;
        }

        session.Worker = null;
        try
        {
            if (worker.Session is { } running)
            {
                if (!running.Completion.IsCompleted)
                {
                    try
                    {
                        await running.SendAsync(new WorkerCommand(WorkerMessageTypes.End));
                        await Task.WhenAny(running.Completion, Task.Delay(TimeSpan.FromSeconds(3)));
                    }
                    catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
                    {
                        // Already gone.
                    }
                }

                await running.DisposeAsync();
            }
        }
        finally
        {
            worker.Lease.Dispose();
        }
    }

    /// <summary>Another job wants the graphics card: a live session on the card ends at once and carries on from the processor.</summary>
    private void OnGpuWanted(object? sender, EventArgs e)
    {
        if (_session?.Worker is { OnGpu: true, Session: { } running } worker)
        {
            worker.LettingGo = true;
            _session!.GaveUpGpu = true;
            LogLettingGoOfGpu();
            _ = running.DisposeAsync().AsTask();
        }
    }

    private void Publish(Session session, string state, string? note, bool force = false)
    {
        var engine = session.Worker is { } worker ? $"Local · {(worker.OnGpu ? "GPU" : "CPU")} · {worker.ModelName}" : session.LastEngine;
        session.LastEngine = engine;
        List<LiveTranscriptSegment> lines;
        lock (_lines)
        {
            lines = _lines.Count > MaxLinesSent ? _lines.GetRange(_lines.Count - MaxLinesSent, MaxLinesSent) : [.. _lines];
        }

        var payload = new LiveTranscriptPayload(session.SessionId, lines, state, engine, note);
        var shown = (state, engine, note, lines.Count);
        if (!force && session.Shown == shown && ++session.Quiet < 10)
        {
            return;
        }

        // Said again every few seconds while nothing changes, so a page that reloads catches up.
        session.Quiet = 0;
        session.Shown = shown;
        _sink.Post(BridgeEventPublisher.Serialize(BridgeEventNames.RecordingLiveTranscript, payload, LiveBridgeJsonContext.Default.BridgeEventEnvelopeLiveTranscriptPayload));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Live transcript: session {SessionId} started")]
    private partial void LogSessionStarted(string sessionId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Live transcript: session {SessionId} ended after {Windows} windows; the draft was discarded")]
    private partial void LogSessionEnded(string sessionId, int windows);

    [LoggerMessage(Level = LogLevel.Information, Message = "Live transcript: {ModelId} loaded (graphics card: {OnGpu})")]
    private partial void LogWorkerStarted(string modelId, bool onGpu);

    [LoggerMessage(Level = LogLevel.Information, Message = "Live transcript: skipped {Skipped} windows to catch up, at window {Window}")]
    private partial void LogSkipped(int skipped, int window);

    [LoggerMessage(Level = LogLevel.Information, Message = "Live transcript: letting go of the graphics card for another job")]
    private partial void LogLettingGoOfGpu();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live transcript: the worker is started again ({Why})")]
    private partial void LogWorkerRestarted(string why);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Live transcript stopped; recording is not affected")]
    private partial void LogStepFailed(Exception exception);

    private sealed class Session(string sessionId, string recordingId, string folder)
    {
        public string SessionId { get; } = sessionId;

        public string RecordingId { get; } = recordingId;

        public string Folder { get; } = folder;

        public LiveWorker? Worker { get; set; }

        public int Next { get; set; }

        public int Windows { get; set; }

        public int Restarts { get; set; }

        public bool Failed { get; set; }

        public bool Off { get; set; }

        /// <summary>It let go of the card for another job once: it stays on the processor for the rest of the session.</summary>
        public bool GaveUpGpu { get; set; }

        public string? LastEngine { get; set; }

        public (string State, string? Engine, string? Note, int Lines)? Shown { get; set; }

        public int Quiet { get; set; }
    }
}
