using Memento.Core.Processing;
using Memento.Core.Recovery;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Library;

/// <summary>
/// Opens the library: checks the folder is there (<see cref="LibraryAvailability"/>), opens or rebuilds the index,
/// recovers interrupted recordings and resumes queued processing. Runs at launch before the window shows, and again
/// the first time the library is used after a missing drive came back.
/// </summary>
public sealed partial class LibraryOpener(
    LibraryAvailability availability,
    ILibraryLocation library,
    ILibraryIndex index,
    RecoveryService recovery,
    ProcessingOrchestrator processing,
    ILogger<LibraryOpener> logger) : IDisposable
{
    private readonly ILogger<LibraryOpener> _logger = logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Raised (on the opening thread) after the library opened, at launch or when its drive came back.</summary>
    public event EventHandler? Opened;

    /// <returns><c>false</c> when the library folder is not available (nothing else was done).</returns>
    public async Task<bool> OpenAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!availability.Check())
            {
                LogUnavailable(library.Root, availability.Unavailable ?? string.Empty);
                return false;
            }

            await index.InitializeAsync(cancellationToken);
            var recovered = await recovery.RunAsync(cancellationToken);

            // Stages that were queued or running when Memento closed continue in the background.
            var resumed = await processing.ResumePendingAsync(cancellationToken);
            LogOpened(library.Root, recovered.Count, resumed);
            Opened?.Invoke(this, EventArgs.Empty);
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Before the library is used: throws <c>library.unavailable</c> (saying <paramref name="refusal"/>) while the folder is
    /// missing, and opens it once if it has come back.
    /// </summary>
    public async Task EnsureOpenAsync(string refusal, CancellationToken cancellationToken)
    {
        if (availability.EnsureAvailable(refusal))
        {
            LogBack(library.Root);
            await OpenAsync(cancellationToken);
        }
    }

    public void Dispose() => _gate.Dispose();

    [LoggerMessage(Level = LogLevel.Information, Message = "Library {Root} ready; {Recovered} interrupted recordings recovered, {Resumed} queued processing stages resumed")]
    private partial void LogOpened(string root, int recovered, int resumed);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Library {Root} is not available, so it was not opened (and not created): {Problem}")]
    private partial void LogUnavailable(string root, string problem);

    [LoggerMessage(Level = LogLevel.Information, Message = "Library {Root} is available again; opening it")]
    private partial void LogBack(string root);
}
