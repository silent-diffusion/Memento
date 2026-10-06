using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>recording.pause</c>: stops writing, keeps capture open.</summary>
public sealed class RecordingPauseMethod(RecordingCoordinator recordings) : BridgeMethod<SessionParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.RecordingPause;

    public override JsonTypeInfo<SessionParams> ParamsTypeInfo => BridgeJsonContext.Default.SessionParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(SessionParams parameters, CancellationToken cancellationToken)
    {
        await recordings.PauseAsync(parameters.SessionId, cancellationToken);
        return new EmptyResult();
    }
}
