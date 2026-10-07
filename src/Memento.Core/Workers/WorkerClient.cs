using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Workers;

/// <summary>
/// Runs one job in a fresh worker process and speaks the JSON-lines protocol with it: sends <c>start</c>, passes every
/// <c>device</c>/<c>track</c>/<c>progress</c> line to the caller as it arrives, and returns the <c>result</c>. On
/// cancellation it sends <c>cancel</c> and kills the process if it has not exited within a few seconds. A process
/// that ends without a result raises <see cref="WorkerCrashedException"/>, so a native abort never reaches the app; so
/// does one that sends nothing for <see cref="WorkerClientOptions.QuietLimit"/> (it is stopped as hung, so a stuck
/// native call cannot hold the graphics card forever).
/// Jobs that use the graphics card run one at a time: a second one waits until the first worker has exited.
/// </summary>
public sealed partial class WorkerClient(IWorkerLauncher launcher, ILogger<WorkerClient> logger, WorkerClientOptions? options = null) : IDisposable
{
    internal const int TailLines = 8;
    internal const int TailLineChars = 300;
    internal const int TailTotalChars = 2000;

    private static readonly TimeSpan CancelGrace = TimeSpan.FromSeconds(5);

    private readonly ConcurrentDictionary<int, IWorkerProcess> _running = new();
    private readonly SemaphoreSlim _gpu = new(1, 1);
    private readonly ILogger<WorkerClient> _logger = logger;
    private readonly WorkerClientOptions _options = options ?? WorkerClientOptions.Default;

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
        if (!job.UsesGpu)
        {
            return await RunOneAsync(job, onReply, cancellationToken);
        }

        // One job on the graphics card at a time (ARCHITECTURE.md §6); the gate opens only once the previous worker has
        // exited and let go of its video memory. The worker also takes a machine-wide lock (WorkerRuntimes.GpuLockName).
        if (!_gpu.Wait(0, CancellationToken.None))
        {
            LogWaitingForGpu();
            await _gpu.WaitAsync(cancellationToken);
        }

        try
        {
            return await RunOneAsync(job, onReply, cancellationToken);
        }
        finally
        {
            _gpu.Release();
        }
    }

    private async Task<WorkerReply> RunOneAsync(WorkerJob job, Func<WorkerReply, Task>? onReply, CancellationToken cancellationToken)
    {
        using var process = launcher.Start();
        _running[process.Id] = process;
        try
        {
            try
            {
                await SendAsync(process, new WorkerCommand(WorkerMessageTypes.Start, job));
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The worker died as it started (a missing native DLL, a loader abort): its stdin pipe is already closed.
                var code = await WaitForExitAsync(process);
                LogCrashed(process.Id, code, FormatTail(process.ErrorTail));
                throw new WorkerCrashedException(code, process.ErrorTail);
            }

            await using var registration = cancellationToken.Register(() => _ = StopAsync(process));
            while (true)
            {
                string? line;
                var read = process.Output.ReadLineAsync(CancellationToken.None).AsTask();
                try
                {
                    line = await read.WaitAsync(_options.QuietLimit, CancellationToken.None);
                }
                catch (IOException)
                {
                    line = null;
                }
                catch (TimeoutException)
                {
                    // Nothing at all for the quiet limit: a native call is stuck. Stop it so the next job can run.
                    _ = read.ContinueWith(t => _ = t.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
                    LogHung(process.Id, (long)_options.QuietLimit.TotalSeconds);
                    process.Kill();
                    var code = await WaitGoneAsync(process);
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new WorkerCrashedException(_options.QuietLimit, code, process.ErrorTail);
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
            LogCrashed(process.Id, exitCode, FormatTail(process.ErrorTail));
            throw new WorkerCrashedException(exitCode, process.ErrorTail);
        }
        finally
        {
            _running.TryRemove(process.Id, out _);
            if (!process.Exited.IsCompleted)
            {
                // Leaving early (an error while handling a line): the worker must be gone before the next one starts.
                process.Kill();
                await WaitGoneAsync(process);
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

    /// <summary>Waits for a killed worker to exit; its exit code, or -1 if it is still there after <see cref="WorkerClientOptions.KillGrace"/>.</summary>
    private async Task<int> WaitGoneAsync(IWorkerProcess process)
    {
        try
        {
            return await process.Exited.WaitAsync(_options.KillGrace);
        }
        catch (TimeoutException)
        {
            LogNotGone(process.Id, _options.KillGrace.TotalSeconds);
            return -1;
        }
    }

    /// <summary>
    /// The last <see cref="TailLines"/> stderr lines for the crash log, each cut to <see cref="TailLineChars"/>
    /// characters and the whole to <see cref="TailTotalChars"/>: native libraries can print a whole buffer (a prompt, a
    /// transcript window) on one line, and the log must stay a diagnostic, not a copy of the user's content.
    /// </summary>
    internal static string FormatTail(IReadOnlyList<string> tail)
    {
        var lines = tail.TakeLast(TailLines)
            .Select(line => new string(line.Select(c => char.IsControl(c) ? ' ' : c).ToArray()))
            .Select(line => line.Length > TailLineChars ? string.Concat(line.AsSpan(0, TailLineChars), "…") : line);
        var joined = string.Join(" | ", lines);
        return joined.Length > TailTotalChars ? string.Concat("(earlier text cut) …", joined.AsSpan(joined.Length - TailTotalChars)) : joined;
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

    private async Task<int> WaitForExitAsync(IWorkerProcess process)
    {
        try
        {
            return await process.Exited.WaitAsync(_options.ExitGrace);
        }
        catch (TimeoutException)
        {
            process.Kill();
            return await WaitGoneAsync(process);
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

    [LoggerMessage(Level = LogLevel.Information, Message = "A worker job waits for the graphics card: another one is using it")]
    private partial void LogWaitingForGpu();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker {Pid} was killed but had not exited after {GraceSeconds} s")]
    private partial void LogNotGone(int pid, double graceSeconds);

    [LoggerMessage(Level = LogLevel.Error, Message = "Worker {Pid} sent nothing for {QuietSeconds} s and was stopped as hung")]
    private partial void LogHung(int pid, long quietSeconds);

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
