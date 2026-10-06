using System.IO;
using Memento.Core.Library;
using Memento.Core.Recovery;
using Microsoft.Extensions.Logging;

namespace Memento.App.Hosting;

/// <summary>
/// Runs before the window shows: opens (or rebuilds) the library index, then recovers interrupted recordings.
/// A failure here is logged and never stops Memento from opening; the UI asks <c>recovery.list</c> itself.
/// </summary>
internal sealed partial class LibraryStartup(
    ILibraryLocation library,
    ILibraryIndex index,
    RecoveryService recovery,
    ILogger<LibraryStartup> logger)
{
    private readonly ILogger<LibraryStartup> _logger = logger;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            Directory.CreateDirectory(library.Root);
            await index.InitializeAsync(cancellationToken);
            var recovered = await recovery.RunAsync(cancellationToken);
            LogDone(recovered.Count);
        }
#pragma warning disable CA1031 // Opening the window matters more; the next launch tries again.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(ex, library.Root);
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Library ready; {Recovered} interrupted recordings recovered")]
    private partial void LogDone(int recovered);

    [LoggerMessage(Level = LogLevel.Error, Message = "Opening the library at {Root} failed; Memento continues without recovery this time")]
    private partial void LogFailed(Exception exception, string root);
}
