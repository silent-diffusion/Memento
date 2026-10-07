using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Workers;

/// <summary>
/// Starts <c>Memento.Worker.exe</c> with redirected stdin/stdout (the protocol) and stderr (diagnostics, kept as a
/// short tail), below-normal priority and no window. Every worker is put in a kill-on-close job object, so Windows ends
/// it when Memento exits however it exits (closed, crashed or killed); the worker also stops when its stdin closes.
/// </summary>
public sealed partial class ProcessWorkerLauncher : IWorkerLauncher, IDisposable
{
    private readonly WorkerLocation _location;
    private readonly ILogger<ProcessWorkerLauncher> _logger;
    private readonly Interop.JobObject? _job;

    public ProcessWorkerLauncher(WorkerLocation location, ILogger<ProcessWorkerLauncher> logger)
    {
        _location = location;
        _logger = logger;
        try
        {
            _job = new Interop.JobObject();
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Workers still stop when their stdin closes; only a hard kill of Memento could leave one running.
            LogNoJob(ex);
        }
    }

    /// <summary>Whether the process <paramref name="pid"/> runs in the job that ends with Memento (tests).</summary>
    internal bool IsInJob(int pid)
    {
        using var process = Process.GetProcessById(pid);
        return _job?.Contains(process) ?? false;
    }

    /// <summary>Ends every worker still running (Windows does the same when Memento exits).</summary>
    public void Dispose() => _job?.Dispose();

    public IWorkerProcess Start()
    {
        var location = _location;
        if (!File.Exists(location.ExecutablePath))
        {
            throw new WorkerUnavailableException($"The transcription worker is missing from this installation ({location.ExecutablePath}).");
        }

        var info = new ProcessStartInfo(location.ExecutablePath)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            StandardInputEncoding = new UTF8Encoding(false),
            WorkingDirectory = Path.GetDirectoryName(location.ExecutablePath)!,
        };

        Process process;
        try
        {
            process = Process.Start(info) ?? throw new WorkerUnavailableException("Windows did not start the transcription worker.");
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            throw new WorkerUnavailableException($"Windows refused to start the transcription worker: {ex.Message}", ex);
        }

        try
        {
            _job?.Assign(process);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            LogNotInJob(ex, process.Id);
        }

        try
        {
            process.PriorityClass = ProcessPriorityClass.BelowNormal;
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or PlatformNotSupportedException)
        {
            // It may have exited already; the client notices.
        }

        LogStarted(process.Id);
        return new WorkerProcess(process);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Worker process {Pid} started")]
    private partial void LogStarted(int pid);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No job object for the workers; a worker could outlive Memento if Memento is killed")]
    private partial void LogNoJob(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Worker process {Pid} could not be put in the job object")]
    private partial void LogNotInJob(Exception exception, int pid);

    private sealed class WorkerProcess : IWorkerProcess
    {
        private const int TailLines = 40;
        private readonly Process _process;
        private readonly Queue<string> _tail = new();
        private readonly TaskCompletionSource<int> _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public WorkerProcess(Process process)
        {
            _process = process;
            Id = process.Id;
            _process.EnableRaisingEvents = true;
            _process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is null)
                {
                    return;
                }

                lock (_tail)
                {
                    _tail.Enqueue(e.Data.Length > 400 ? e.Data[..400] : e.Data);
                    while (_tail.Count > TailLines)
                    {
                        _tail.Dequeue();
                    }
                }
            };
            _process.Exited += (_, _) => _ = CompleteExitAsync();
            _process.BeginErrorReadLine();
            if (_process.HasExited)
            {
                _ = CompleteExitAsync();
            }
        }

        public int Id { get; }

        public TextWriter Input => _process.StandardInput;

        public TextReader Output => _process.StandardOutput;

        public Task<int> Exited => _exited.Task;

        public TimeSpan CpuTime => _process.HasExited ? TimeSpan.Zero : _process.TotalProcessorTime;

        public IReadOnlyList<string> ErrorTail
        {
            get
            {
                lock (_tail)
                {
                    return _tail.ToList();
                }
            }
        }

        public void Kill()
        {
            try
            {
                _process.Kill(entireProcessTree: true);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException)
            {
                // Already exited.
            }
        }

        public void Dispose()
        {
            try
            {
                _process.StandardInput.Close();
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException)
            {
                // The pipe is already closed.
            }

            _process.Dispose();
        }

        private async Task CompleteExitAsync()
        {
            // Let the stderr reader drain before the exit is reported.
            await _process.WaitForExitAsync();
            _exited.TrySetResult(_process.ExitCode);
        }
    }
}
