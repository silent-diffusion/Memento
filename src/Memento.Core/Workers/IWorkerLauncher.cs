namespace Memento.Core.Workers;

/// <summary>Starts one worker process per job (<c>Memento.Worker.exe</c>; tests use a scripted fake).</summary>
public interface IWorkerLauncher
{
    /// <exception cref="WorkerUnavailableException">The worker cannot be started (missing or blocked).</exception>
    IWorkerProcess Start();
}
