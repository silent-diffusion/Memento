using Memento.Core.Workers;

namespace Memento.Worker;

/// <summary>
/// The machine-wide lock a worker holds while it uses the graphics card (<see cref="WorkerRuntimes.GpuLockName"/>), so
/// two workers never share it: another Memento, a tool, or a worker left over from a host that was killed. A mutex
/// belongs to a thread, so a dedicated thread takes it and keeps it until <see cref="Dispose"/>; if the process ends
/// first, Windows releases it (the next worker sees it abandoned and takes it).
/// </summary>
internal sealed class GpuLock : IDisposable
{
    private static readonly TimeSpan Poll = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan Reminder = TimeSpan.FromMinutes(5);

    private readonly ManualResetEventSlim _release;
    private readonly Thread _thread;

    private GpuLock(ManualResetEventSlim release, Thread thread)
    {
        _release = release;
        _thread = thread;
    }

    /// <summary>Waits for the lock (telling the host that it waits, and again every few minutes).</summary>
    /// <exception cref="OperationCanceledException">Cancelled while waiting.</exception>
    public static GpuLock Acquire(ProtocolWriter output, CancellationToken cancellationToken)
    {
        var name = Environment.GetEnvironmentVariable(WorkerRuntimes.GpuLockVariable) is { Length: > 0 } custom ? custom : WorkerRuntimes.GpuLockName;
        var acquired = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new ManualResetEventSlim();
        var thread = new Thread(() =>
        {
            using var mutex = new Mutex(false, name);
            System.Diagnostics.Stopwatch? told = null;
            while (true)
            {
                bool owned;
                try
                {
                    owned = mutex.WaitOne(Poll);
                }
                catch (AbandonedMutexException)
                {
                    // The previous holder ended without letting go; the lock is ours now.
                    owned = true;
                }

                if (owned)
                {
                    break;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    acquired.TrySetCanceled(cancellationToken);
                    return;
                }

                // Said again every few minutes: the host stops a worker that is silent for its quiet limit as hung.
                if (told is null || told.Elapsed >= Reminder)
                {
                    output.Log(told is null ? "Waiting for another worker to finish with the graphics card." : "Still waiting for another worker to finish with the graphics card.");
                    told = System.Diagnostics.Stopwatch.StartNew();
                }
            }

            acquired.TrySetResult();
            release.Wait();
            mutex.ReleaseMutex();
        })
        {
            IsBackground = true,
            Name = "GPU lock",
        };
        thread.Start();
        try
        {
            acquired.Task.GetAwaiter().GetResult();
        }
        catch (TaskCanceledException ex)
        {
            release.Dispose();
            throw new OperationCanceledException("Cancelled while waiting for the graphics card.", ex, cancellationToken);
        }

        return new GpuLock(release, thread);
    }

    public void Dispose()
    {
        _release.Set();
        _thread.Join(TimeSpan.FromSeconds(2));
        _release.Dispose();
    }
}
