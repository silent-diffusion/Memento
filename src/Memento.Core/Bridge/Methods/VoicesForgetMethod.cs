using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Voices;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>voices.forget</c> (2.0): Forget: removes the voice's signature at once (not undoable).</summary>
public sealed class VoicesForgetMethod(KnownVoiceService voices) : BridgeMethod<VoiceIdParams, KnownVoicesResult>
{
    public override string Name => BridgeMethodNames.VoicesForget;

    public override JsonTypeInfo<VoiceIdParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.VoiceIdParams;

    public override JsonTypeInfo<KnownVoicesResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.KnownVoicesResult;

    public override Task<KnownVoicesResult> InvokeAsync(VoiceIdParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return voices.ForgetAsync(parameters.VoiceId, cancellationToken);
    }
}
