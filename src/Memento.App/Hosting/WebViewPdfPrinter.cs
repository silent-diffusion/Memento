using System.IO;
using System.Windows;
using System.Windows.Interop;
using Memento.Core;
using Memento.Documents.Export;
using Microsoft.Web.WebView2.Core;

namespace Memento.App.Hosting;

/// <summary>
/// Prints the document's print HTML to PDF with WebView2 <c>PrintToPdfAsync</c> (ARCHITECTURE.md §1: the PDF comes from
/// the same markup the viewer shows). A hidden WebView2 controller, parented to the main window and never shown, loads the
/// page from a temporary file (scripts off, nothing else reachable) and prints it with the style's paper size and
/// margins, backgrounds on and Chromium's own header and footer off; the page's <c>@page</c> rules add the running
/// header and page numbers. The temporary files are removed afterwards.
/// </summary>
internal sealed class WebViewPdfPrinter : IPdfPrinter
{
    private static readonly TimeSpan LoadTimeout = TimeSpan.FromSeconds(30);

    public async Task<byte[]> PrintAsync(string html, PdfPrintOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(html);
        ArgumentNullException.ThrowIfNull(options);
        var application = Application.Current ?? throw new InvalidOperationException("PDF export needs the Memento window.");
        var work = Path.Combine(Path.GetTempPath(), "Memento-print-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(work);
        try
        {
            var page = Path.Combine(work, "document.html");
            var pdf = Path.Combine(work, "document.pdf");
            await File.WriteAllTextAsync(page, PrintPagePolicy.Apply(html), cancellationToken);
            var printed = await application.Dispatcher.InvokeAsync(() => PrintOnUiThreadAsync(application, page, pdf, options, cancellationToken)).Task.Unwrap();
            if (!printed || !File.Exists(pdf))
            {
                throw new IOException("WebView2 did not produce the PDF.");
            }

            return await File.ReadAllBytesAsync(pdf, cancellationToken);
        }
        finally
        {
            try
            {
                Directory.Delete(work, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A scanner may still hold the file; %TEMP% is cleaned by Windows.
            }
        }
    }

    private static async Task<bool> PrintOnUiThreadAsync(Application application, string page, string pdf, PdfPrintOptions options, CancellationToken cancellationToken)
    {
        var window = application.MainWindow ?? throw new InvalidOperationException("PDF export needs the Memento window.");
        var handle = new WindowInteropHelper(window).EnsureHandle();
        var environment = await WebViewEnvironmentFactory.CreateAsync();
        var controller = await environment.CreateCoreWebView2ControllerAsync(handle);
        try
        {
            controller.IsVisible = false;
            var core = controller.CoreWebView2;
            core.Settings.IsScriptEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsWebMessageEnabled = false;
            core.Settings.AreHostObjectsAllowed = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.IsReputationCheckingRequired = false; // shared with the main WebView's user data folder
            var pageUri = new Uri(page).AbsoluteUri;

            // Only the page itself: no link, refresh or frame may take the printer anywhere else, nor open a window.
            core.NavigationStarting += (_, e) => e.Cancel = !string.Equals(e.Uri, pageUri, StringComparison.OrdinalIgnoreCase);
            core.FrameNavigationStarting += (_, e) => e.Cancel = true;
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.DownloadStarting += (_, e) =>
            {
                e.Cancel = true;
                e.Handled = true;
            };
            var loaded = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            core.NavigationCompleted += (_, e) => loaded.TrySetResult(e.IsSuccess);
            core.Navigate(pageUri);
            using (cancellationToken.Register(() => loaded.TrySetCanceled(cancellationToken)))
            {
                if (!await loaded.Task.WaitAsync(LoadTimeout, cancellationToken))
                {
                    throw new IOException("The document page did not load for printing.");
                }
            }

            var settings = environment.CreatePrintSettings();
            settings.Orientation = CoreWebView2PrintOrientation.Portrait;
            settings.PageWidth = options.PageWidthInches;
            settings.PageHeight = options.PageHeightInches;
            settings.MarginTop = options.MarginTopInches;
            settings.MarginBottom = options.MarginBottomInches;
            settings.MarginLeft = options.MarginLeftInches;
            settings.MarginRight = options.MarginRightInches;
            settings.ShouldPrintBackgrounds = options.PrintBackgrounds;
            settings.ShouldPrintHeaderAndFooter = false;
            settings.ShouldPrintSelectionOnly = false;
            return await core.PrintToPdfAsync(pdf, settings);
        }
        finally
        {
            controller.Close();
        }
    }
}
