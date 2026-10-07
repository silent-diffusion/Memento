using System.Text.Json;

namespace Memento.Core.Workers;

/// <summary>
/// A started worker job (<see cref="WorkerClient.OpenAsync"/>): further lines can be sent to it while it runs, and
/// <see cref="Completion"/> ends with its result or its failure. Disposing a session whose job has not ended stops the
/// worker.
/// </summary>
public sealed class WorkerSession : IAsyncDisposable
{
    private readonly IWorkerProcess _process;
    private readonly SemaphoreSlim _writer;

    internal WorkerSession(IWorkerProcess process, Task<WorkerReply> completion, SemaphoreSlim writer)
    {
        _process = process;
        _writer = writer;
        Completion = completion;
    }

    /// <summary>The <c>result</c>; throws <see cref="WorkerJobException"/>, <see cref="WorkerCrashedException"/> or <see cref="OperationCanceledException"/> as <see cref="WorkerClient.RunAsync"/> does.</summary>
    public Task<WorkerReply> Completion { get; }

    /// <summary>Sends one line to the worker.</summary>
    /// <exception cref="IOException">The worker has gone.</exception>
    public Task SendAsync(WorkerCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return SendAsync(_process, _writer, command);
    }

    public async ValueTask DisposeAsync()
    {
        if (!Completion.IsCompleted)
        {
            try
            {
                _process.Kill();
            }
            catch (InvalidOperationException)
            {
                // It ended meanwhile.
            }
        }

        try
        {
            await Completion;
        }
#pragma warning disable CA1031 // The caller that wanted the outcome awaited Completion itself; disposing only waits for the end.
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    /// <summary>One line on the worker's input; lines from different callers (a command, a cancel) never interleave.</summary>
    internal static async Task SendAsync(IWorkerProcess process, SemaphoreSlim writer, WorkerCommand command)
    {
        var json = JsonSerializer.Serialize(command, WorkerJsonContext.Default.WorkerCommand);
        await writer.WaitAsync(CancellationToken.None);
        try
        {
            await process.Input.WriteLineAsync(json);
            await process.Input.FlushAsync();
        }
        finally
        {
            writer.Release();
        }
    }
}
