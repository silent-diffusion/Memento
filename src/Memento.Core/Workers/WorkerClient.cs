using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Workers;

/// <summary>
/// Runs one job in a fresh worker process and speaks the JSON-lines protocol with it: sends <c>start</c>, passes every
/// <c>device</c>/<c>track</c>/<c>progress</c> line to the caller as it arrives, and returns the <c>result</c>. On
/// cancellation it sends <c>cancel</c> and kills the process if it has not exited within a few seconds. A process
/// that ends without a result raises <see cref="WorkerCrashedException"/>, so a native abort never reaches the app.
/// Jobs that use the graphics card run one at a time: a second one waits until the first worker has exited.
/// </summary>
public sealed partial class WorkerClient(IWorkerLauncher launcher, ILogger<WorkerClient> logger) : IDisposable
{
    private static readonly TimeSpan CancelGrace = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan ExitGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan KillGrace = TimeSpan.FromSeconds(10);

    private readonly ConcurrentDictionary<int, IWorkerProcess> _running = new();
    private readonly SemaphoreSlim _gpu = new(1, 1);
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
        await using var session = await OpenAsync(job, onReply, cancellationToken);
        return await session.Completion;
    }

    /// <summary>
    /// Starts <paramref name="job"/> and returns at once with a session that can send the worker further lines (a local
    /// model job that stays loaded takes <c>prompts</c> and <c>end</c>); <see cref="WorkerSession.Completion"/> ends with
    /// the result, or throws as <see cref="RunAsync"/> does. A job on the graphics card holds the card until it ends.
    /// </summary>
    /// <exception cref="WorkerUnavailableException">The worker could not be started.</exception>
    /// <exception cref="OperationCanceledException">Cancelled while waiting for the graphics card.</exception>
    public async Task<WorkerSession> OpenAsync(WorkerJob job, Func<WorkerReply, Task>? onReply, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        cancellationToken.ThrowIfCancellationRequested();
        var gpu = job.UsesGpu;
        if (gpu && !_gpu.Wait(0, CancellationToken.None))
        {
            // One job on the graphics card at a time (ARCHITECTURE.md §6); the gate opens only once the previous worker has
            // exited and let go of its video memory. The worker also takes a machine-wide lock (WorkerRuntimes.GpuLockName).
            LogWaitingForGpu();
            await _gpu.WaitAsync(cancellationToken);
        }

        IWorkerProcess process;
        try
        {
            process = launcher.Start();
        }
        catch
        {
            if (gpu)
            {
                _gpu.Release();
            }

            throw;
        }

        _running[process.Id] = process;
        var writer = new SemaphoreSlim(1, 1);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completion = PumpAsync(process, job, onReply, writer, started, gpu, cancellationToken);
        await Task.WhenAny(started.Task, completion);
        return new WorkerSession(process, completion, writer);
    }

    /// <summary>Sends <c>start</c>, then reads the worker's lines until the final one; always lets go of the process and the card.</summary>
    private async Task<WorkerReply> PumpAsync(IWorkerProcess process, WorkerJob job, Func<WorkerReply, Task>? onReply, SemaphoreSlim writer, TaskCompletionSource started, bool gpu, CancellationToken cancellationToken)
    {
        try
        {
            await WorkerSession.SendAsync(process, writer, new WorkerCommand(WorkerMessageTypes.Start, job));
            started.TrySetResult();
            await using var registration = cancellationToken.Register(() => _ = StopAsync(process, writer));
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
            started.TrySetResult();
            _running.TryRemove(process.Id, out _);
            if (!process.Exited.IsCompleted)
            {
                // Leaving early (an error while handling a line): the worker must be gone before the next one starts.
                process.Kill();
                await WaitGoneAsync(process);
            }

            process.Dispose();
            if (gpu)
            {
                _gpu.Release();
            }
        }
    }

    public void Dispose() => _gpu.Dispose();

    /// <summary>
    /// Memento is closing: ends every worker at once instead of waiting out the cancel grace period (finished windows
    /// and tracks are already saved; the jobs are cancelled first, so they report a cancel, not a crash).
    /// </summary>
    public void KillAll()
    {
        foreach (var process in _running.Values)
        {
            process.Kill();
        }
    }

    private async Task WaitGoneAsync(IWorkerProcess process)
    {
        try
        {
            await process.Exited.WaitAsync(KillGrace);
        }
        catch (TimeoutException)
        {
            LogNotGone(process.Id);
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

    private async Task StopAsync(IWorkerProcess process, SemaphoreSlim writer)
    {
        try
        {
            await WorkerSession.SendAsync(process, writer, new WorkerCommand(WorkerMessageTypes.Cancel));
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

    [LoggerMessage(Level = LogLevel.Information, Message = "A worker job waits for the graphics card: another one is using it")]
    private partial void LogWaitingForGpu();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker {Pid} was killed but had not exited after 10 s")]
    private partial void LogNotGone(int pid);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Worker {Pid} {Level}: {Message}")]
    private partial void LogWorkerLine(int pid, string level, string message);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Worker wrote a {Length}-character line that is not part of the protocol")]
    private partial void LogStrayLine(int length);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker wrote a damaged protocol line")]
    private partial void LogBadLine(Exception exception);

    // Expected after a cancel during native work that cannot be interrupted (a sherpa-onnx track); nothing is lost,
    // because finished windows and tracks are kept, so this is not a warning.
    [LoggerMessage(Level = LogLevel.Information, Message = "Worker {Pid} was still busy in native code 5 s after the cancel and was ended")]
    private partial void LogKilled(int pid);

    [LoggerMessage(Level = LogLevel.Error, Message = "Worker {Pid} exited with code {ExitCode} without a result; last diagnostics: {Tail}")]
    private partial void LogCrashed(int pid, int exitCode, string tail);
}
