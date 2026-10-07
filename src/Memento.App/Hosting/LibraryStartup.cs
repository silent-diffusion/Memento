using Memento.Core.Library;
using Microsoft.Extensions.Logging;

namespace Memento.App.Hosting;

/// <summary>
/// Runs before the window shows: opens (or rebuilds) the library index, then recovers interrupted recordings
/// (<see cref="LibraryOpener"/>). A missing library on another drive is reported in the window, never re-created
/// empty. A failure here is logged and never stops Memento from opening; the UI asks <c>recovery.list</c> itself.
/// </summary>
internal sealed partial class LibraryStartup(LibraryOpener opener, ILibraryLocation library, ILogger<LibraryStartup> logger)
{
    private readonly ILogger<LibraryStartup> _logger = logger;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await opener.OpenAsync(cancellationToken);
        }
#pragma warning disable CA1031 // Opening the window matters more; the next launch tries again.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            LogFailed(ex, library.Root);
        }
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Opening the library at {Root} failed; Memento continues without recovery this time")]
    private partial void LogFailed(Exception exception, string root);
}
