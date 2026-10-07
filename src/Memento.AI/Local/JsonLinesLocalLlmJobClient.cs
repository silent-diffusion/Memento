using System.Text.Json;
using Memento.Core.Workers;

namespace Memento.AI.Local;

/// <summary>
/// The host side of the local job protocol over any channel: sends <c>start</c>, passes <c>device</c> and
/// <c>progress</c> lines on, sends <c>cancel</c> when cancelled, and returns the <c>result</c>. A channel that ends
/// without a final line is a crashed worker (<see cref="AiErrorCodes.WorkerCrashed"/>).
/// </summary>
public sealed class JsonLinesLocalLlmJobClient(Func<CancellationToken, Task<LocalLlmChannel>> connect) : ILocalLlmJobClient
{
    public async Task<LocalLlmResult> RunAsync(LocalLlmJob job, IProgress<LocalLlmWorkerReply>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        await using var channel = await connect(cancellationToken);
        using var gate = new SemaphoreSlim(1, 1);
        async Task SendAsync(LocalLlmWorkerCommand command)
        {
            await gate.WaitAsync(CancellationToken.None);
            try
            {
                await channel.ToWorker.WriteAsync(JsonSerializer.Serialize(command, LocalLlmJsonContext.Default.LocalLlmWorkerCommand) + "\n");
                await channel.ToWorker.FlushAsync(CancellationToken.None);
            }
            finally
            {
                gate.Release();
            }
        }

        await SendAsync(new LocalLlmWorkerCommand(WorkerMessageTypes.Start, new LocalLlmWorkerJob(LocalLlmWorkerJob.LlmKind, job)));
        await using var registration = cancellationToken.Register(() => _ = SafeCancelAsync());
        while (true)
        {
            string? line;
            try
            {
                line = await channel.FromWorker.ReadLineAsync(CancellationToken.None);
            }
            catch (IOException)
            {
                line = null;
            }

            if (line is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw new LocalLlmException(AiErrors.WorkerCrashed(LocalAiProvider.ProviderName, job.ModelName, "the worker ended without an answer"));
            }

            LocalLlmWorkerReply? reply;
            try
            {
                reply = JsonSerializer.Deserialize(line, LocalLlmJsonContext.Default.LocalLlmWorkerReply);
            }
            catch (JsonException)
            {
                // A native library wrote to the protocol stream; skip the line.
                continue;
            }

            switch (reply?.Type)
            {
                case WorkerMessageTypes.Result when reply.Llm is { } result:
                    return result;
                case WorkerMessageTypes.Error:
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new LocalLlmException(new AiError(reply.Code ?? AiErrorCodes.ProviderError, LocalAiProvider.ProviderName, reply.Message ?? "The local model failed."));
                case WorkerMessageTypes.Cancelled:
                    throw new OperationCanceledException("The local model stopped as asked.", cancellationToken);
                case WorkerMessageTypes.Device or WorkerMessageTypes.Progress:
                    progress?.Report(reply);
                    break;
                default:
                    break;
            }
        }

        async Task SafeCancelAsync()
        {
            try
            {
                await SendAsync(new LocalLlmWorkerCommand(WorkerMessageTypes.Cancel));
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The worker has already gone.
            }
        }
    }
}
