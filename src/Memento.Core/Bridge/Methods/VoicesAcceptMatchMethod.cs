using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Voices;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>voices.acceptMatch</c> (2.0): "Use name": renames the speaker and refines the known voice.</summary>
public sealed class VoicesAcceptMatchMethod(KnownVoiceService voices) : BridgeMethod<VoiceAcceptParams, VoiceAcceptResult>
{
    public override string Name => BridgeMethodNames.VoicesAcceptMatch;

    public override JsonTypeInfo<VoiceAcceptParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.VoiceAcceptParams;

    public override JsonTypeInfo<VoiceAcceptResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.VoiceAcceptResult;

    public override Task<VoiceAcceptResult> InvokeAsync(VoiceAcceptParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        return voices.AcceptAsync(parameters.RecordingId, parameters.SpeakerId, parameters.VoiceId, cancellationToken);
    }
}
