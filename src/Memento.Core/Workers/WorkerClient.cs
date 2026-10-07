using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Workers;

/// <summary>
/// Runs one job in a fresh worker process and speaks the JSON-lines protocol with it: sends <c>start</c>, passes every
/// <c>device</c>/<c>track</c>/<c>progress</c> line to the caller as it arrives, and returns the <c>result</c>. On
/// cancellation it sends <c>cancel</c> and kills the process if it has not exited within a few seconds. A process
/// that ends without a result raises <see cref="WorkerCrashedException"/>, so a native abort never reaches the app.
/// </summary>
public sealed partial class WorkerClient(IWorkerLauncher launcher, ILogger<WorkerClient> logger)
{
    private static readonly TimeSpan CancelGrace = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(3);

    private readonly ConcurrentDictionary<int, IWorkerProcess> _running = new();
    private readonly ILogger<WorkerClient> _logger = logger;

    /// <summary>Processor time used by the workers running now.</summary>
    public TimeSpan RunningCpuTime => _running.Values.Aggregate(TimeSpan.Zero, (sum, p) => sum + SafeCpu(p));

    /// <summary>Runs <paramref name="job"/> to its result.</summary>
    /// <param name="onReply">Called in order for every non-final line; awaited before the next line is read.</param>
    /// <exception cref="WorkerJobException">The worker reported an error.</exception>
    /// <exception cref="WorkerCrashedException">The worker exited without a result.</exception>
    /// <exception cref="WorkerUnavailableException">The worker could not be started.</exception>
    /// <exception cref="OperationCanceledException">Cancelled; the worker has been stopped.</exception>
    public async Task<WorkerReply> RunAsync(WorkerJob job, Func<WorkerReply, Task>? onReply, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        using var process = launcher.Start();
        _running[process.Id] = process;
        try
        {
            await SendAsync(process, new WorkerCommand(WorkerMessageTypes.Start, job));
            await using var registration = cancellationToken.Register(() => _ = StopAsync(process));
            while (true)
            {
                string? line;
                try
                {
                    line = await process.Output.ReadLineAsync(CancellationToken.None);
                }
                catch (IOException)
                {
                    line = null;
                }

                if (line is null)
                {
                    break;
                }

                var reply = Parse(line);
                if (reply is null)
                {
                    continue;
                }

                switch (reply.Type)
                {
                    case WorkerMessageTypes.Result:
                        await WaitForExitAsync(process);
                        return reply;
                    case WorkerMessageTypes.Error:
                        await WaitForExitAsync(process);
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new WorkerJobException(reply.Code ?? WorkerErrorCodes.Engine, reply.Message ?? "The worker reported an error without a message.");
                    case WorkerMessageTypes.Cancelled:
                        await WaitForExitAsync(process);
                        throw new OperationCanceledException("The worker stopped as asked.", cancellationToken);
                    case WorkerMessageTypes.Log:
                        LogWorkerLine(process.Id, reply.Level ?? "info", reply.Message ?? string.Empty);
                        break;
                    default:
                        if (onReply is not null)
                        {
                            await onReply(reply);
                        }

                        break;
                }
            }

            var exitCode = await WaitForExitAsync(process);
            cancellationToken.ThrowIfCancellationRequested();
            LogCrashed(process.Id, exitCode, string.Join(" | ", process.ErrorTail.TakeLast(8)));
            throw new WorkerCrashedException(exitCode, process.ErrorTail);
        }
        finally
        {
            _running.TryRemove(process.Id, out _);
            if (!process.Exited.IsCompleted)
            {
                process.Kill();
            }
        }
    }

    private static TimeSpan SafeCpu(IWorkerProcess process)
    {
        try
        {
            return process.CpuTime;
        }
        catch (InvalidOperationException)
        {
            return TimeSpan.Zero;
        }
    }

    private static async Task SendAsync(IWorkerProcess process, WorkerCommand command)
    {
        var json = JsonSerializer.Serialize(command, WorkerJsonContext.Default.WorkerCommand);
        await process.Input.WriteLineAsync(json);
        await process.Input.FlushAsync();
    }

    private static async Task<int> WaitForExitAsync(IWorkerProcess process)
    {
        try
        {
            return await process.Exited.WaitAsync(ExitGrace);
        }
        catch (TimeoutException)
        {
            process.Kill();
            return await process.Exited;
        }
    }

    private WorkerReply? Parse(string line)
    {
        // Native libraries may write to the same stream; only protocol objects count.
        if (!line.StartsWith("{\"type\":", StringComparison.Ordinal))
        {
            if (line.Length > 0)
            {
                LogStrayLine(line.Length);
            }

            return null;
        }

        try
        {
            return JsonSerializer.Deserialize(line, WorkerJsonContext.Default.WorkerReply);
        }
        catch (JsonException ex)
        {
            LogBadLine(ex);
            return null;
        }
    }

    private async Task StopAsync(IWorkerProcess process)
    {
        try
        {
            await SendAsync(process, new WorkerCommand(WorkerMessageTypes.Cancel));
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
        {
            // Already gone.
        }

        if (await Task.WhenAny(process.Exited, Task.Delay(CancelGrace)) != process.Exited)
        {
            LogKilled(process.Id);
            process.Kill();
        }
    }

    [LoggerMessage(Level = LogLevel.Debug, Message = "Worker {Pid} {Level}: {Message}")]
    private partial void LogWorkerLine(int pid, string level, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Worker wrote a {Length}-character line that is not part of the protocol")]
    private partial void LogStrayLine(int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker wrote a damaged protocol line")]
    private partial void LogBadLine(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker {Pid} did not stop within the grace period and was killed")]
    private partial void LogKilled(int pid);

    [LoggerMessage(Level = LogLevel.Error, Message = "Worker {Pid} exited with code {ExitCode} without a result; last diagnostics: {Tail}")]
    private partial void LogCrashed(int pid, int exitCode, string tail);
}
