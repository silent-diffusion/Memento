namespace Memento.Core.Updates;

/// <summary>
/// The update feed and installer behind <see cref="UpdateService"/>: Velopack over the project's GitHub releases in the
/// app, a fake in tests. Implementations decide which releases count (pre-releases only for a pre-release build).
/// </summary>
public interface IUpdateClient
{
    /// <summary>The running version (SemVer).</summary>
    string CurrentVersion { get; }

    /// <summary>
    /// <c>false</c> when this copy cannot update itself (run from a build folder or the portable zip, or updates are
    /// switched off for a test); <see cref="UpdateService"/> then never checks.
    /// </summary>
    bool CanUpdate { get; }

    /// <summary>A version already downloaded and waiting for a restart (from an earlier run, too), or <c>null</c>.</summary>
    string? PendingVersion { get; }

    /// <summary>Asks the feed for a newer version; <c>null</c> when this is the newest.</summary>
    /// <exception cref="UpdateCheckException">The feed could not be reached or read.</exception>
    Task<string?> CheckAsync(CancellationToken cancellationToken);

    /// <summary>Downloads the version the last <see cref="CheckAsync"/> found, reporting 0–100.</summary>
    /// <exception cref="UpdateCheckException">The download failed.</exception>
    Task DownloadAsync(IProgress<int> progress, CancellationToken cancellationToken);

    /// <summary>Closes Memento, installs the downloaded version and starts it again. Does not return when it works.</summary>
    void ApplyAndRestart();
}
