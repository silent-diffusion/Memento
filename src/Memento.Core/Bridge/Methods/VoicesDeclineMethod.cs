using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Voices;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>voices.decline</c> (2.0): "Not {name}": hides a suggestion in this recording (or shows it again).</summary>
public sealed class VoicesDeclineMethod(KnownVoiceService voices) : BridgeMethod<VoiceDeclineParams, VoiceMatchesResult>
{
    public override string Name => BridgeMethodNames.VoicesDecline;

    public override JsonTypeInfo<VoiceDeclineParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.VoiceDeclineParams;

    public override JsonTypeInfo<VoiceMatchesResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.VoiceMatchesResult;

    public override async Task<VoiceMatchesResult> InvokeAsync(VoiceDeclineParams parameters, CancellationToken cancellationToken) =>
        new VoiceMatchesResult(await voices.DeclineAsync(parameters.RecordingId, parameters.VoiceId, parameters.Declined, cancellationToken));
}
