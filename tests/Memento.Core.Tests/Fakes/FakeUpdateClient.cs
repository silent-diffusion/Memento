using Memento.Core.Updates;

namespace Memento.Core.Tests.Fakes;

/// <summary>An update feed under test control: what a check finds, how a download goes, and what was asked.</summary>
internal sealed class FakeUpdateClient : IUpdateClient
{
    private int _checks;
    private int _downloads;
    private int _applies;

    public string CurrentVersion { get; init; } = "0.5.0";

    public bool CanUpdate { get; init; } = true;

    public string? PendingVersion { get; set; }

    /// <summary>The version a check finds (null: none), or an exception to throw.</summary>
    public string? Available { get; set; }

    public Exception? CheckFailure { get; set; }

    public Exception? DownloadFailure { get; set; }

    /// <summary>Held until released, so a test can act in the middle of a download.</summary>
    public TaskCompletionSource? DownloadGate { get; set; }

    public int Checks => Volatile.Read(ref _checks);

    public int Downloads => Volatile.Read(ref _downloads);

    public int Applies => Volatile.Read(ref _applies);

    public Task<string?> CheckAsync(CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _checks);
        return CheckFailure is { } failure ? Task.FromException<string?>(failure) : Task.FromResult(Available);
    }

    public async Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _downloads);
        progress.Report(0);
        progress.Report(42);
        if (DownloadGate is { } gate)
        {
            await gate.Task.WaitAsync(cancellationToken);
        }

        if (DownloadFailure is { } failure)
        {
            throw failure;
        }

        progress.Report(100);
        PendingVersion = Available;
    }

    public void ApplyAndRestart() => Interlocked.Increment(ref _applies);
}
