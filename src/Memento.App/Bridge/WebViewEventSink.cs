using System.Runtime.InteropServices;
using System.Windows.Threading;
using Memento.Core.Host;
using Microsoft.Web.WebView2.Core;

namespace Memento.App.Bridge;

/// <summary>
/// Delivers host events to the page with <c>PostWebMessageAsJson</c> on the UI thread.
/// Kept separate from <see cref="WebViewBridge"/> so event publishers never depend on the router.
/// </summary>
internal sealed class WebViewEventSink : IBridgeEventSink
{
    private volatile CoreWebView2? _webView;
    private volatile Dispatcher? _dispatcher;

    /// <summary>Starts delivering to <paramref name="webView"/>; events before this are dropped (the page reads state on start).</summary>
    public void Attach(CoreWebView2 webView, Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _webView = webView;
    }

    /// <summary>
    /// Stops delivering: the window is closing and its WebView2 control is disposed with it, while host services keep
    /// publishing until the host has stopped. Events after this are dropped.
    /// </summary>
    public void Detach()
    {
        _webView = null;
    }

    public void Post(string eventJson)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        dispatcher.BeginInvoke(() => TryPost(_webView, eventJson));
    }

    /// <summary>
    /// Posts to the page unless it is gone. A queued event can still run after the control was disposed (the window
    /// closed between the queueing and the delivery); WebView2 then throws, and that must never become a crash report.
    /// </summary>
    internal static bool TryPost(CoreWebView2? webView, string json)
    {
        if (webView is null)
        {
            return false;
        }

        try
        {
            webView.PostWebMessageAsJson(json);
            return true;
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException or ObjectDisposedException)
        {
            return false;
        }
    }
}
