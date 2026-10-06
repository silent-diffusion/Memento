using System.ComponentModel;
using System.Diagnostics;
using Memento.Core.Host;
using Microsoft.Extensions.Logging;

namespace Memento.App.Hosting;

/// <summary>Opens links and Windows settings pages through the shell. The URI was validated by the bridge method.</summary>
internal sealed partial class ExternalLauncher(ILogger<ExternalLauncher> logger) : IExternalLauncher
{
    private readonly ILogger<ExternalLauncher> _logger = logger;

    public bool TryOpen(Uri uri)
    {
        ArgumentNullException.ThrowIfNull(uri);
        try
        {
            using var process = Process.Start(new ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true });
            LogOpened(uri.Scheme);
            return true;
        }
        catch (Win32Exception ex)
        {
            LogFailed(ex, uri.Scheme);
            return false;
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Opened an external {Scheme} target")]
    private partial void LogOpened(string scheme);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Windows could not open an external {Scheme} target")]
    private partial void LogFailed(Exception exception, string scheme);
}
