using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Workers;

/// <summary>
/// Starts <c>Memento.Worker.exe</c> with redirected stdin/stdout (the protocol) and stderr (diagnostics, kept as a
/// short tail), below-normal priority and no window. The worker exits when its stdin closes, so it never outlives the app.
/// </summary>
public sealed partial class ProcessWorkerLauncher(WorkerLocation location, ILogger<ProcessWorkerLauncher> logger) : IWorkerLauncher
{
    private readonly ILogger<ProcessWorkerLauncher> _logger = logger;

    public IWorkerProcess Start()
    {
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
