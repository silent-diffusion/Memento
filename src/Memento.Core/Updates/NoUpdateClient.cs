namespace Memento.Core.Updates;

/// <summary>For copies that cannot update themselves (tests, build folders): never checks, never downloads.</summary>
public sealed class NoUpdateClient(string currentVersion) : IUpdateClient
{
    public string CurrentVersion { get; } = currentVersion;

    public bool CanUpdate => false;

    public string? PendingVersion => null;

    public Task<string?> CheckAsync(CancellationToken cancellationToken) => Task.FromResult<string?>(null);

    public Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken) => Task.CompletedTask;

    public void ApplyAndRestart()
    {
    }
}
