using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Voices;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>voices.matches</c> (2.0): the known voices the recording's unnamed speakers sound like.</summary>
public sealed class VoicesMatchesMethod(KnownVoiceService voices) : BridgeMethod<RecordingIdParams, VoiceMatchesResult>
{
    public override string Name => BridgeMethodNames.VoicesMatches;

    public override JsonTypeInfo<RecordingIdParams> ParamsTypeInfo => ReviewBridgeJsonContext.Default.RecordingIdParams;

    public override JsonTypeInfo<VoiceMatchesResult> ResultTypeInfo => ReviewBridgeJsonContext.Default.VoiceMatchesResult;

    public override async Task<VoiceMatchesResult> InvokeAsync(RecordingIdParams parameters, CancellationToken cancellationToken) =>
        new VoiceMatchesResult(await voices.MatchesAsync(parameters.RecordingId, cancellationToken));
}
