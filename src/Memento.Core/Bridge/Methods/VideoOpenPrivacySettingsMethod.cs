using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>video.openPrivacySettings</c> (2.0): opens Windows Settings at Privacy &amp; security › Camera
/// (<c>ms-settings:privacy-webcam</c>) or at the screen capture page (<c>ms-settings:privacy-graphicscaptureprogrammatic</c>),
/// the fix the §18.6 camera-privacy and capture-blocked cards offer.
/// </summary>
public sealed class VideoOpenPrivacySettingsMethod(IExternalLauncher launcher) : BridgeMethod<VideoOpenPrivacySettingsParams, EmptyResult>
{
    public const string CameraPage = "ms-settings:privacy-webcam";
    public const string ScreenCapturePage = "ms-settings:privacy-graphicscaptureprogrammatic";

    public override string Name => BridgeMethodNames.VideoOpenPrivacySettings;

    public override JsonTypeInfo<VideoOpenPrivacySettingsParams> ParamsTypeInfo => VideoBridgeJsonContext.Default.VideoOpenPrivacySettingsParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => VideoBridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(VideoOpenPrivacySettingsParams parameters, CancellationToken cancellationToken)
    {
        var page = parameters.Which switch
        {
            "camera" => CameraPage,
            "screenCapture" => ScreenCapturePage,
            _ => throw new BridgeException(BridgeErrorCodes.InvalidParams, $"'which' is 'camera' or 'screenCapture', not '{parameters.Which}'.", parameters.Which),
        };

        if (!launcher.TryOpen(new Uri(page)))
        {
            throw new BridgeException(
                DomainErrorCodes.AppOpenExternalFailed,
                parameters.Which == "camera"
                    ? "Windows did not open its camera privacy settings. Nothing was changed; open Settings › Privacy & security › Camera yourself."
                    : "Windows did not open its screen capture privacy settings. Nothing was changed; open Settings › Privacy & security yourself.",
                page);
        }

        return Task.FromResult(new EmptyResult());
    }
}
