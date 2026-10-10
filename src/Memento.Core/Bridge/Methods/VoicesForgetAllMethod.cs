using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Voices;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>voices.forgetAll</c> (2.0): Forget all: removes <c>voices/known.json</c> (not undoable).</summary>
public sealed class VoicesForgetAllMethod(KnownVoiceService voices) : BridgeMethod<EmptyParams, KnownVoicesResult>
{
    public override string Name => BridgeMethodNames.VoicesForgetAll;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<KnownVoicesResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.KnownVoicesResult;

    public override Task<KnownVoicesResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        voices.ForgetAllAsync(cancellationToken);
}
