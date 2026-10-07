using Memento.Core;
using Microsoft.Web.WebView2.Core;

namespace Memento.App.Hosting;

/// <summary>
/// The one WebView2 environment configuration for the window and the PDF printer (WebView2 refuses a second environment
/// on the same user data folder with different options).
/// <list type="bullet">
/// <item>Custom crash reporting: a renderer crash dump holds page memory (transcripts, documents), so dumps stay on this PC
/// instead of being uploaded to Microsoft's crash service (nothing leaves the PC without an explicit action).</item>
/// <item>No single sign-on with the Windows account and no browser extensions: the page never signs in anywhere.</item>
/// </list>
/// </summary>
internal static class WebViewEnvironmentFactory
{
    public static Task<CoreWebView2Environment> CreateAsync()
    {
        var options = new CoreWebView2EnvironmentOptions(
            additionalBrowserArguments: null,
            language: null,
            targetCompatibleBrowserVersion: null,
            allowSingleSignOnUsingOSPrimaryAccount: false)
        {
            IsCustomCrashReportingEnabled = true,
            AreBrowserExtensionsEnabled = false,
        };
        return CoreWebView2Environment.CreateAsync(browserExecutableFolder: null, userDataFolder: AppPaths.WebView2UserData, options);
    }
}
