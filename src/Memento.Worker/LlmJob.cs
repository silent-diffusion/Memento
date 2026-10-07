using System.Text.Json;
using Memento.AI.Local;
using Memento.Core.Workers;

namespace Memento.Worker;

/// <summary>
/// The <c>llm</c> job (document generation with the local model): Memento.AI's <see cref="LocalLlmJobRunner"/> on the
/// LLamaSharp engine (<see cref="LlamaLocalLlmEngineFactory"/>). The job loads the CPU build of llama.cpp when it is
/// sent with <c>device: "cpu"</c> (faster on the processor than the Vulkan build with no layers offloaded) and takes
/// the machine-wide GPU lock otherwise, so it never shares the graphics card with a transcription.
/// </summary>
internal sealed class LlmJob(ProtocolWriter output)
{
    public async Task<WorkerReply> RunAsync(JsonElement body, CancellationToken cancellationToken)
    {
        LocalLlmJob? job;
        try
        {
            job = body.Deserialize(LocalLlmJsonContext.Default.LocalLlmJob);
        }
        catch (JsonException ex)
        {
            throw new WorkerFailure(WorkerErrorCodes.InvalidJob, "The local model job could not be read: " + ex.Message, ex);
        }

        if (job is null)
        {
            throw new WorkerFailure(WorkerErrorCodes.InvalidJob, "The local model job is empty.");
        }

        using var gpuLock = job.Device != LocalLlmDevices.Cpu ? GpuLock.Acquire(output, cancellationToken) : null;
        var runner = new LocalLlmJobRunner(
            new LlamaLocalLlmEngineFactory(new ProtocolLogger<LlamaLocalLlmEngineFactory>(output)),
            new ProtocolLogger<LocalLlmJobRunner>(output));
        try
        {
            var result = await runner.RunAsync(job, line => output.Send(ToWorker(line)), cancellationToken);
            return new WorkerReply
            {
                Type = WorkerMessageTypes.Result,
                Llm = JsonSerializer.SerializeToElement(result, LocalLlmJsonContext.Default.LocalLlmResult),
            };
        }
        catch (LocalLlmException ex)
        {
            output.Log(ex.Code + ": " + (ex.Error.Diagnostic ?? "-"));
            throw new WorkerFailure(ex.Code, ex.Message, ex);
        }
        catch (ArgumentException ex)
        {
            throw new WorkerFailure(WorkerErrorCodes.InvalidJob, ex.Message, ex);
        }
    }

    /// <summary>The local protocol's <c>device</c> and <c>progress</c> lines as Core worker lines.</summary>
    internal static WorkerReply ToWorker(LocalLlmWorkerReply line) => new()
    {
        Type = line.Type,
        Percent = line.Percent,
        LlmDevice = line.LlmDevice is { } device ? JsonSerializer.SerializeToElement(device, LocalLlmJsonContext.Default.LocalLlmDeviceInfo) : null,
        LlmProgress = line.LlmProgress is { } progress ? JsonSerializer.SerializeToElement(progress, LocalLlmJsonContext.Default.LocalLlmProgress) : null,
        Code = line.Code,
        Message = line.Message,
        Level = line.Level,
    };
}
