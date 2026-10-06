using System.Windows.Threading;
using Memento.Core.Bridge;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;

namespace Memento.App.Bridge;

/// <summary>
/// Connects WebView2 messaging to <see cref="BridgeRouter"/>: requests arrive through
/// <c>WebMessageReceived</c> and are answered with <c>PostWebMessageAsJson</c>; events go out through
/// <see cref="WebViewEventSink"/>. Messages from any origin other than the app's virtual host are dropped.
/// </summary>
internal sealed partial class WebViewBridge(
    BridgeRouter router,
    WebViewEventSink events,
    IHostApplicationLifetime lifetime,
    ILogger<WebViewBridge> logger)
{
    public const string VirtualHost = "app.memento";
    public const string Origin = "https://" + VirtualHost + "/";

    private readonly ILogger<WebViewBridge> _logger = logger;
    private CoreWebView2? _webView;

    /// <summary>Starts routing requests and events for <paramref name="webView"/>. Call on the UI thread.</summary>
    public void Attach(CoreWebView2 webView, Dispatcher dispatcher)
    {
        ArgumentNullException.ThrowIfNull(webView);
        _webView = webView;
        webView.WebMessageReceived += OnWebMessageReceived;
        events.Attach(webView, dispatcher);
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!e.Source.StartsWith(Origin, StringComparison.Ordinal))
        {
            LogForeignMessage(e.Source);
            return;
        }

        // The router turns every failure, cancellation included, into a structured response.
        var response = await router.HandleAsync(e.WebMessageAsJson, lifetime.ApplicationStopping);
        if (!lifetime.ApplicationStopping.IsCancellationRequested)
        {
            _webView?.PostWebMessageAsJson(response);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Dropped a bridge message from unexpected origin {Origin}")]
    private partial void LogForeignMessage(string origin);
}
