using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Voices;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>voices.remember</c> (2.0): learns or refines a named speaker's voice when "Remember speakers by voice" is on.</summary>
public sealed class VoicesRememberMethod(KnownVoiceService voices) : BridgeMethod<VoiceRememberParams, VoiceRememberResult>
{
    public override string Name => BridgeMethodNames.VoicesRemember;

    public override JsonTypeInfo<VoiceRememberParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.VoiceRememberParams;

    public override JsonTypeInfo<VoiceRememberResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.VoiceRememberResult;

    public override Task<VoiceRememberResult> InvokeAsync(VoiceRememberParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return voices.RememberAsync(parameters.RecordingId, parameters.SpeakerId, cancellationToken);
    }
}
