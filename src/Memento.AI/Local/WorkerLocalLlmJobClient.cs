using System.Text.Json;
using Memento.Core.Workers;

namespace Memento.AI.Local;

/// <summary>
/// Runs local model jobs in Memento.Worker through Core's <see cref="WorkerClient"/>: one process per job (a native
/// failure cannot take the app down, and video memory is released when the job ends), the same one-job-on-the-GPU gate
/// as transcription, the job and its replies carried as raw JSON in Core's worker records (<c>kind: "llm"</c>). A session
/// (<see cref="OpenAsync"/>) is one such job that keeps the model loaded across batches of prompts.
/// </summary>
public sealed class WorkerLocalLlmJobClient(WorkerClient workers) : ILocalLlmJobClient
{
    public async Task<LocalLlmResult> RunAsync(LocalLlmJob job, IProgress<LocalLlmWorkerReply>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var body = JsonSerializer.SerializeToElement(job, LocalLlmJsonContext.Default.LocalLlmJob);
        WorkerReply reply;
        try
        {
            reply = await workers.RunAsync(
                new WorkerJob(WorkerJobKinds.Llm, Llm: body),
                line =>
                {
                    if (progress is not null && ToLocal(line) is { } local)
                    {
                        progress.Report(local);
                    }

                    return Task.CompletedTask;
                },
                cancellationToken);
        }
        catch (Exception ex) when (Map(ex, job.ModelName) is { } mapped)
        {
            throw mapped;
        }

        return ResultOf(reply, job.ModelName);
    }

    /// <summary>Starts a worker job that loads the model once and answers each batch of prompts until the session is disposed.</summary>
    public async Task<ILocalLlmSession> OpenAsync(LocalLlmJob job, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        var body = JsonSerializer.SerializeToElement(job with { Session = true, Prompts = [] }, LocalLlmJsonContext.Default.LocalLlmJob);
        var session = new WorkerLlmSession(job.ModelName);
        try
        {
            session.Attach(await workers.OpenAsync(new WorkerJob(WorkerJobKinds.Llm, Llm: body), session.OnLineAsync, cancellationToken));
        }
        catch (Exception ex) when (Map(ex, job.ModelName) is { } mapped)
        {
            throw mapped;
        }

        return session;
    }

    /// <summary>A worker failure as the local provider's exception, or <c>null</c> for anything else (cancellation).</summary>
    internal static LocalLlmException? Map(Exception exception, string modelName) => exception switch
    {
        WorkerJobException ex => new LocalLlmException(new AiError(ex.Code.StartsWith("ai.", StringComparison.Ordinal) ? ex.Code : AiErrorCodes.ProviderError, LocalAiProvider.ProviderName, ex.Message)),
        WorkerCrashedException ex => new LocalLlmException(AiErrors.WorkerCrashed(LocalAiProvider.ProviderName, modelName, ex.LooksLikeOutOfMemory ? "out of memory" : ex.Message), ex),
        WorkerUnavailableException ex => new LocalLlmException(AiErrors.WorkerCrashed(LocalAiProvider.ProviderName, modelName, "the worker could not be started"), ex),
        _ => null,
    };

    /// <summary>The <c>result</c> or <c>batch</c> line's outputs.</summary>
    internal static LocalLlmResult ResultOf(WorkerReply reply, string modelName)
    {
        ArgumentNullException.ThrowIfNull(reply);
        if (reply.Llm is not { ValueKind: JsonValueKind.Object } result)
        {
            throw new LocalLlmException(AiErrors.WorkerCrashed(LocalAiProvider.ProviderName, modelName, "the worker's result has no answers"));
        }

        return result.Deserialize(LocalLlmJsonContext.Default.LocalLlmResult)
            ?? throw new LocalLlmException(AiErrors.WorkerCrashed(LocalAiProvider.ProviderName, modelName, "the worker's result is empty"));
    }

    /// <summary>A Core worker line as the local protocol's reply, or <c>null</c> for lines that carry nothing for it.</summary>
    internal static LocalLlmWorkerReply? ToLocal(WorkerReply line)
    {
        ArgumentNullException.ThrowIfNull(line);
        if (line.Type is not (WorkerMessageTypes.Device or WorkerMessageTypes.Progress))
        {
            return null;
        }

        return new LocalLlmWorkerReply
        {
            Type = line.Type,
            Percent = line.Percent,
            LlmDevice = line.LlmDevice is { ValueKind: JsonValueKind.Object } device ? device.Deserialize(LocalLlmJsonContext.Default.LocalLlmDeviceInfo) : null,
            LlmProgress = line.LlmProgress is { ValueKind: JsonValueKind.Object } p ? p.Deserialize(LocalLlmJsonContext.Default.LocalLlmProgress) : null,
        };
    }
}
