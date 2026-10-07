using System.Globalization;
using System.Text.Json;
using System.Threading.Channels;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging;

namespace Memento.AI.Local;

/// <summary>
/// Runs a <see cref="LocalLlmJob"/> on an <see cref="ILocalLlmEngineFactory"/>: checks the model file, loads within
/// the budget, runs every prompt in order with streamed progress, and always unloads. <see cref="RunJsonLinesAsync"/>
/// speaks the worker protocol (Memento.Worker's <c>Program.cs</c>): <c>ready</c>, then a <c>start</c> line, then
/// <c>device</c>/<c>progress</c> lines and exactly one of <c>result</c>, <c>error</c> or <c>cancelled</c>; a
/// <c>cancel</c> line or the end of input stops the job.
/// </summary>
public sealed partial class LocalLlmJobRunner(ILocalLlmEngineFactory factory, ILogger<LocalLlmJobRunner> logger)
{
    private readonly ILogger<LocalLlmJobRunner> _logger = logger;

    /// <summary>Exit codes, as Memento.Worker uses them.</summary>
    public const int ExitOk = 0;
    public const int ExitFailed = 1;
    public const int ExitInvalidJob = 2;
    public const int ExitCancelled = 3;

    /// <summary>
    /// Runs the job in this process; <paramref name="send"/> receives <c>device</c> and <c>progress</c> lines, and for a
    /// job that stays loaded (<see cref="LocalLlmJob.Session"/>) a <c>batch</c> line per batch of prompts:
    /// <see cref="LocalLlmJob.Prompts"/> first when there are any, then each batch <paramref name="nextBatch"/> returns
    /// until it returns <c>null</c> (the host's <c>end</c>). The result of a session carries no outputs.
    /// </summary>
    /// <exception cref="LocalLlmException">A specific failure (model missing, not enough video memory, spill, engine failure).</exception>
    /// <exception cref="OperationCanceledException">Cancelled; the model has been unloaded.</exception>
    public async Task<LocalLlmResult> RunAsync(LocalLlmJob job, Action<LocalLlmWorkerReply>? send, CancellationToken cancellationToken, Func<CancellationToken, Task<IReadOnlyList<LocalLlmPrompt>?>>? nextBatch = null)
    {
        ArgumentNullException.ThrowIfNull(job);
        Validate(job);
        if (!File.Exists(job.ModelPath))
        {
            throw new LocalLlmException(AiErrors.ModelNotInstalled(LocalAiProvider.ProviderName, job.ModelName));
        }

        var count = job.TokenizeTexts?.Count ?? job.Prompts.Count;
        void Progress(LocalLlmProgress progress)
        {
            var percent = progress.PromptIndex < 0 || count == 0 ? 0 : Math.Round(100.0 * progress.PromptIndex / count, 1);
            send?.Invoke(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Progress, Percent = percent, LlmProgress = progress });
        }

        async Task<List<LocalLlmOutput>> RunPromptsAsync(ILocalLlmEngine engine, IReadOnlyList<LocalLlmPrompt> prompts)
        {
            count = prompts.Count;
            var outputs = new List<LocalLlmOutput>(prompts.Count);
            for (var i = 0; i < prompts.Count; i++)
            {
                var prompt = prompts[i];
                var index = i;
                var produced = 0;
                Progress(new LocalLlmProgress(LocalLlmProgress.ReadingPrompt, index, count));
                var output = await engine.GenerateAsync(index, prompt, delta => Progress(new LocalLlmProgress(LocalLlmProgress.Generating, index, count, delta, ++produced)), cancellationToken);
                var tokensPerSecond = Math.Round(output.TokensPerSecond, 1);
                LogGenerated(prompt.Purpose, index, output.StopReason, output.PromptTokens, output.OutputTokens, tokensPerSecond);
                outputs.Add(output);
            }

            return outputs;
        }

        Progress(new LocalLlmProgress(LocalLlmProgress.Loading, -1, count));
        LogLoading(job.ModelId, job.Device, count);
        using var engine = await factory.LoadAsync(job, Progress, cancellationToken);
        send?.Invoke(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Device, LlmDevice = engine.Device });
        LogLoaded(job.ModelId, engine.Device.Backend, engine.Device.GpuLayers, engine.Device.ContextTokens, (long)engine.LoadMs, (long)engine.WarmUpMs);

        if (job.TokenizeTexts is { } texts)
        {
            var counts = new List<int>(texts.Count);
            foreach (var text in texts)
            {
                cancellationToken.ThrowIfCancellationRequested();
                counts.Add(engine.CountTokens(text));
            }

            return new LocalLlmResult([], engine.Device, engine.LoadMs, engine.WarmUpMs, engine.DedicatedVramBytes, engine.SharedVramGrowthBytes, counts);
        }

        LocalLlmResult Result(IReadOnlyList<LocalLlmOutput> outputs) =>
            new(outputs, engine.Device, engine.LoadMs, engine.WarmUpMs, engine.DedicatedVramBytes, engine.SharedVramGrowthBytes);

        if (!job.Session)
        {
            return Result(await RunPromptsAsync(engine, job.Prompts));
        }

        if (job.Prompts.Count > 0)
        {
            send?.Invoke(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Batch, Llm = Result(await RunPromptsAsync(engine, job.Prompts)) });
        }

        while (nextBatch is not null && await nextBatch(cancellationToken) is { } prompts)
        {
            ValidatePrompts(prompts);
            send?.Invoke(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Batch, Llm = Result(await RunPromptsAsync(engine, prompts)) });
        }

        return Result([]);
    }

    /// <summary>The worker side of the protocol. Returns the process exit code.</summary>
    public async Task<int> RunJsonLinesAsync(TextReader input, TextWriter output, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(output);
        var gate = new object();
        void Send(LocalLlmWorkerReply reply)
        {
            var json = JsonSerializer.Serialize(reply, LocalLlmJsonContext.Default.LocalLlmWorkerReply);
            lock (gate)
            {
                output.Write(json);
                output.Write('\n');
                output.Flush();
            }
        }

        Send(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Ready, Pid = Environment.ProcessId });
        LocalLlmWorkerCommand? command;
        try
        {
            var line = await input.ReadLineAsync(cancellationToken);
            command = line is null ? null : JsonSerializer.Deserialize(line, LocalLlmJsonContext.Default.LocalLlmWorkerCommand);
        }
        catch (JsonException ex)
        {
            Send(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Error, Code = WorkerErrorCodes.InvalidJob, Message = "The job could not be read: " + ex.Message });
            return ExitInvalidJob;
        }

        if (command is not { Type: WorkerMessageTypes.Start, Job: { Kind: LocalLlmWorkerJob.LlmKind, Llm: { } job } })
        {
            Send(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Error, Code = WorkerErrorCodes.InvalidJob, Message = "The first line must be a start command with an llm job." });
            return ExitInvalidJob;
        }

        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var batches = Channel.CreateUnbounded<IReadOnlyList<LocalLlmPrompt>>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var watcher = Task.Run(
            async () =>
            {
                try
                {
                    while (await input.ReadLineAsync(cancel.Token) is { } next)
                    {
                        var line = ReadCommand(next);
                        if (line?.Type == WorkerMessageTypes.Cancel)
                        {
                            break;
                        }

                        if (line?.Type == WorkerMessageTypes.Prompts && line.Llm is { } batch)
                        {
                            batches.Writer.TryWrite(batch.Prompts);
                        }
                        else if (line?.Type == WorkerMessageTypes.End)
                        {
                            batches.Writer.TryComplete();
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException or OperationCanceledException)
                {
                    // The pipe broke or the job finished: the host has gone or no longer listens.
                }

                await cancel.CancelAsync();
            },
            CancellationToken.None);

        async Task<IReadOnlyList<LocalLlmPrompt>?> NextBatchAsync(CancellationToken token) =>
            await batches.Reader.WaitToReadAsync(token) && batches.Reader.TryRead(out var prompts) ? prompts : null;

        try
        {
            var result = await RunAsync(job, Send, cancel.Token, NextBatchAsync);
            Send(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Result, Llm = result });
            return ExitOk;
        }
        catch (OperationCanceledException)
        {
            Send(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Cancelled });
            return ExitCancelled;
        }
        catch (LocalLlmException ex)
        {
            LogFailed(job.ModelId, ex.Code, ex.Error.Diagnostic ?? "-");
            Send(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Log, Level = "warning", Message = ex.Code + ": " + (ex.Error.Diagnostic ?? "-") });
            Send(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Error, Code = ex.Code, Message = ex.Message });
            return ExitFailed;
        }
        catch (ArgumentException ex)
        {
            Send(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Error, Code = WorkerErrorCodes.InvalidJob, Message = ex.Message });
            return ExitInvalidJob;
        }
#pragma warning disable CA1031 // Every failure becomes a structured error line before the process exits.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogCrashed(job.ModelId, ex.GetType().Name);
            var error = AiErrors.LocalFailed(LocalAiProvider.ProviderName, job.ModelName, ex.GetType().Name);
            Send(new LocalLlmWorkerReply { Type = WorkerMessageTypes.Error, Code = error.Code, Message = error.Message });
            return ExitFailed;
        }
        finally
        {
            await cancel.CancelAsync();
            await Task.WhenAny(watcher, Task.Delay(TimeSpan.FromSeconds(1), CancellationToken.None));
        }
    }

    private static void Validate(LocalLlmJob job)
    {
        if (!LocalLlmDevices.IsValid(job.Device))
        {
            throw new ArgumentException($"Unknown device '{job.Device}'.", nameof(job));
        }

        if (!LocalChatTemplates.IsKnown(job.Profile?.TemplateId))
        {
            throw new ArgumentException($"No verified chat template '{job.Profile?.TemplateId}'.", nameof(job));
        }

        if (job.TokenizeTexts is null && job.Prompts.Count == 0 && !job.Session)
        {
            throw new ArgumentException("The job has no prompts.", nameof(job));
        }

        ValidatePrompts(job.Prompts);
    }

    private static LocalLlmWorkerCommand? ReadCommand(string line)
    {
        try
        {
            return JsonSerializer.Deserialize(line, LocalLlmJsonContext.Default.LocalLlmWorkerCommand);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ValidatePrompts(IReadOnlyList<LocalLlmPrompt> prompts)
    {
        foreach (var prompt in prompts)
        {
            if (prompt.MaxTokens <= 0 || prompt.Messages.Count == 0)
            {
                throw new ArgumentException(string.Create(CultureInfo.InvariantCulture, $"Prompt '{prompt.Purpose}' needs messages and a positive token limit."), nameof(prompts));
            }
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Local model {ModelId}: loading on {Device} for {Count} prompt(s)")]
    private partial void LogLoading(string modelId, string device, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Local model {ModelId}: loaded on {Backend} with {Layers} GPU layers, context {Context}, in {LoadMs} ms (warm-up {WarmUpMs} ms)")]
    private partial void LogLoaded(string modelId, string backend, int layers, int context, long loadMs, long warmUpMs);

    [LoggerMessage(Level = LogLevel.Information, Message = "Local generation for {Purpose} (#{Index}) stopped with {StopReason}: {PromptTokens} prompt and {OutputTokens} output tokens at {TokensPerSecond} tokens/s")]
    private partial void LogGenerated(string purpose, int index, string stopReason, int promptTokens, int outputTokens, double tokensPerSecond);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Local model {ModelId} failed: {Code} ({Diagnostic})")]
    private partial void LogFailed(string modelId, string code, string diagnostic);

    [LoggerMessage(Level = LogLevel.Error, Message = "Local model {ModelId} failed unexpectedly: {ExceptionType}")]
    private partial void LogCrashed(string modelId, string exceptionType);
}
