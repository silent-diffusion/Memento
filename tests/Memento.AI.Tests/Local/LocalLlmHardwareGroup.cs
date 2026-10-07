using Memento.Core.Workers;

namespace Memento.AI.Tests.Local;

/// <summary>
/// Real-model tests run one at a time: one model in memory, one GPU stage at a time. They load models in this process,
/// so the group holds the workers' machine-wide GPU lock while it runs; a worker started by another test assembly
/// (the M4d pipeline test) then waits instead of sharing the card's memory.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalLlmHardwareGroup : ICollectionFixture<MachineGpuLock>
{
    public const string Name = "Local LLM hardware";
}

/// <summary>Holds <see cref="WorkerRuntimes.GpuLockName"/> from creation to disposal (a mutex belongs to a thread, so a dedicated one holds it).</summary>
public sealed class MachineGpuLock : IDisposable
{
    private static readonly TimeSpan Wait = TimeSpan.FromMinutes(30);

    private readonly ManualResetEventSlim _release = new();
    private readonly Thread _thread;

    public MachineGpuLock()
    {
        using var acquired = new ManualResetEventSlim();
        _thread = new Thread(() =>
        {
            using var mutex = new Mutex(false, WorkerRuntimes.GpuLockName);
            bool owned;
            try
            {
                owned = mutex.WaitOne(Wait);
            }
            catch (AbandonedMutexException)
            {
                owned = true;
            }

            acquired.Set();
            if (owned)
            {
                _release.Wait();
                mutex.ReleaseMutex();
            }
        })
        { IsBackground = true, Name = "Machine GPU lock" };
        _thread.Start();
        acquired.Wait();
    }

    public void Dispose()
    {
        _release.Set();
        _thread.Join();
        _release.Dispose();
    }
}
