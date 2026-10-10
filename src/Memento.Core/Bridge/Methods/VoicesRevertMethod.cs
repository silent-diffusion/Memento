using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Voices;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>voices.revert</c> (2.0): takes back an enrolment that <c>voices.remember</c> or <c>voices.acceptMatch</c> made (Undo).</summary>
public sealed class VoicesRevertMethod(KnownVoiceService voices) : BridgeMethod<VoiceChangeParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.VoicesRevert;

    public override JsonTypeInfo<VoiceChangeParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.VoiceChangeParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(VoiceChangeParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        await voices.RevertAsync(parameters.ChangeId, cancellationToken);
        return new EmptyResult();
    }
}
