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

    public void Post(string eventJson)
    {
        var dispatcher = _dispatcher;
        if (dispatcher is null || dispatcher.HasShutdownStarted)
        {
            return;
        }

        dispatcher.BeginInvoke(() => _webView?.PostWebMessageAsJson(eventJson));
    }
}
