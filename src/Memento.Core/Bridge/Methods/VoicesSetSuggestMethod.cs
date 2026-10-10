using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Voices;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>voices.setSuggest</c> (2.0): "Suggest this voice" on or off.</summary>
public sealed class VoicesSetSuggestMethod(KnownVoiceService voices) : BridgeMethod<VoiceSuggestParams, KnownVoicesResult>
{
    public override string Name => BridgeMethodNames.VoicesSetSuggest;

    public override JsonTypeInfo<VoiceSuggestParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.VoiceSuggestParams;

    public override JsonTypeInfo<KnownVoicesResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.KnownVoicesResult;

    public override Task<KnownVoicesResult> InvokeAsync(VoiceSuggestParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return voices.SetSuggestAsync(parameters.VoiceId, parameters.Suggest, cancellationToken);
    }
}
