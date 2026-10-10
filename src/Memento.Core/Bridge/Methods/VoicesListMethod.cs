using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Voices;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>voices.list</c> (2.0): Settings › Known voices: every known voice and whether remembering is on.</summary>
public sealed class VoicesListMethod(KnownVoiceService voices) : BridgeMethod<EmptyParams, KnownVoicesResult>
{
    public override string Name => BridgeMethodNames.VoicesList;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<KnownVoicesResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.KnownVoicesResult;

    public override Task<KnownVoicesResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        voices.ListAsync(cancellationToken);
}
