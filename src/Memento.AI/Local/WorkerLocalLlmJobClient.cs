using System.Text.Json;
using Memento.Core.Workers;

namespace Memento.AI.Local;

/// <summary>
/// Runs local model jobs in Memento.Worker through Core's <see cref="WorkerClient"/>: one process per job (a native
/// failure cannot take the app down, and video memory is released when the job ends), the same one-job-on-the-GPU gate
/// as transcription, the job and its replies carried as raw JSON in Core's worker records (<c>kind: "llm"</c>).
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
        catch (WorkerJobException ex)
        {
            throw new LocalLlmException(new AiError(ex.Code.StartsWith("ai.", StringComparison.Ordinal) ? ex.Code : AiErrorCodes.ProviderError, LocalAiProvider.ProviderName, ex.Message));
        }
        catch (WorkerCrashedException ex)
        {
            throw new LocalLlmException(AiErrors.WorkerCrashed(LocalAiProvider.ProviderName, job.ModelName, ex.LooksLikeOutOfMemory ? "out of memory" : ex.Message), ex);
        }
        catch (WorkerUnavailableException ex)
        {
            throw new LocalLlmException(AiErrors.WorkerCrashed(LocalAiProvider.ProviderName, job.ModelName, "the worker could not be started"), ex);
        }

        if (reply.Llm is not { ValueKind: JsonValueKind.Object } result)
        {
            throw new LocalLlmException(AiErrors.WorkerCrashed(LocalAiProvider.ProviderName, job.ModelName, "the worker's result has no answers"));
        }

        LocalLlmResult? answers;
        try
        {
            answers = result.Deserialize(LocalLlmJsonContext.Default.LocalLlmResult);
        }
        catch (JsonException ex)
        {
            throw new LocalLlmException(AiErrors.WorkerCrashed(LocalAiProvider.ProviderName, job.ModelName, "the worker's result could not be read"), ex);
        }

        return answers ?? throw new LocalLlmException(AiErrors.WorkerCrashed(LocalAiProvider.ProviderName, job.ModelName, "the worker's result is empty"));
    }

    /// <summary>
    /// A Core worker line as the local protocol's reply, or <c>null</c> for lines that carry nothing for it. A device or
    /// progress object that does not have the expected shape is left out rather than failing the job.
    /// </summary>
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
            LlmDevice = line.LlmDevice is { ValueKind: JsonValueKind.Object } device ? TryRead(device, LocalLlmJsonContext.Default.LocalLlmDeviceInfo) : null,
            LlmProgress = line.LlmProgress is { ValueKind: JsonValueKind.Object } p ? TryRead(p, LocalLlmJsonContext.Default.LocalLlmProgress) : null,
        };
    }

    private static T? TryRead<T>(JsonElement element, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo)
        where T : class
    {
        try
        {
            return element.Deserialize(typeInfo);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
