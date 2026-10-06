using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>app.openExternal</c>: opens an <c>https:</c> link in the default browser or an <c>ms-settings:</c> page
/// (e.g. <c>ms-settings:privacy-microphone</c>). Every other scheme is refused, so the page can never
/// launch programs or open local files.
/// </summary>
public sealed class OpenExternalMethod(IExternalLauncher launcher) : BridgeMethod<OpenExternalParams, OpenExternalResult>
{
    public const string UnsupportedTargetCode = DomainErrorCodes.AppOpenExternalUnsupportedTarget;
    public const string LaunchFailedCode = DomainErrorCodes.AppOpenExternalFailed;

    private const int MaxUrlLength = 2048;

    public override string Name => BridgeMethodNames.AppOpenExternal;

    public override JsonTypeInfo<OpenExternalParams> ParamsTypeInfo => BridgeJsonContext.Default.OpenExternalParams;

    public override JsonTypeInfo<OpenExternalResult> ResultTypeInfo => BridgeJsonContext.Default.OpenExternalResult;

    public override Task<OpenExternalResult> InvokeAsync(OpenExternalParams parameters, CancellationToken cancellationToken)
    {
        var uri = Validate(parameters.Url);
        if (!launcher.TryOpen(uri))
        {
            throw new BridgeException(
                LaunchFailedCode,
                $"Windows could not open {Describe(uri)}. Nothing in Memento was changed.");
        }

        return Task.FromResult(new OpenExternalResult(Opened: true));
    }

    internal static Uri Validate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > MaxUrlLength)
        {
            throw new BridgeException(UnsupportedTargetCode, "Memento needs a link of at most 2048 characters to open.");
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new BridgeException(UnsupportedTargetCode, "That link is not a complete address, so Memento did not open it.");
        }

        var allowed = uri.Scheme switch
        {
            "https" => !string.IsNullOrEmpty(uri.Host) && string.IsNullOrEmpty(uri.UserInfo),
            "ms-settings" => true,
            _ => false,
        };

        if (!allowed)
        {
            throw new BridgeException(
                UnsupportedTargetCode,
                $"Memento only opens https:// links and Windows settings pages, so the '{uri.Scheme}:' link was not opened.");
        }

        return uri;
    }

    private static string Describe(Uri uri) =>
        uri.Scheme == "ms-settings" ? "that Windows settings page" : uri.Host;
}
