using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Memento.Core.Host;
using Microsoft.Extensions.Logging;

namespace Memento.App.Hosting;

/// <summary>
/// <see cref="IClipboard"/> with the WPF clipboard on the window's thread: Unicode text, the Windows <c>HTML Format</c>
/// when there is formatted content (<see cref="ClipboardHtml"/>), and <c>CanUploadToCloudClipboard</c> = 0 so Windows'
/// "sync across your devices" never uploads what Memento copies. The data is flushed, so it stays after Memento closes.
/// </summary>
internal sealed partial class WpfClipboard(ILogger<WpfClipboard> logger) : IClipboard
{
    /// <summary>The registered format Windows reads before it syncs clipboard content to the cloud (a DWORD; 0 = never).</summary>
    private const string CanUploadToCloudClipboard = "CanUploadToCloudClipboard";

    private readonly ILogger<WpfClipboard> _logger = logger;

    public Task SetAsync(ClipboardContent content, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var application = Application.Current
            ?? throw new ClipboardUnavailableException("The clipboard needs the Memento window.");
        return application.Dispatcher.InvokeAsync(
            () =>
            {
                var data = new DataObject();
                data.SetData(DataFormats.UnicodeText, content.Text);
                if (content.Html is { } html)
                {
                    data.SetData(DataFormats.Html, ClipboardHtml.Wrap(html));
                }

                data.SetData(CanUploadToCloudClipboard, new MemoryStream(BitConverter.GetBytes(0)));
                try
                {
                    // WPF retries opening a clipboard another program holds (about a second) before it gives up.
                    Clipboard.SetDataObject(data, copy: true);
                }
                catch (Exception ex) when (ex is COMException or ExternalException)
                {
                    LogRefused(ex.HResult);
                    throw new ClipboardUnavailableException($"Windows answered 0x{ex.HResult:X8}.", ex);
                }
            },
            System.Windows.Threading.DispatcherPriority.Normal,
            cancellationToken).Task;
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The clipboard refused a copy (0x{HResult:X8})")]
    private partial void LogRefused(int hResult);
}
