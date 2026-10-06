using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>recording.stop</c>: returns once finalize has started; <c>recording.state</c> then reports <c>finalizing</c> and <c>ready</c>.</summary>
public sealed class RecordingStopMethod(RecordingCoordinator recordings) : BridgeMethod<SessionParams, RecordingStopResult>
{
    public override string Name => BridgeMethodNames.RecordingStop;

    public override JsonTypeInfo<SessionParams> ParamsTypeInfo => BridgeJsonContext.Default.SessionParams;

    public override JsonTypeInfo<RecordingStopResult> ResultTypeInfo => BridgeJsonContext.Default.RecordingStopResult;

    public override async Task<RecordingStopResult> InvokeAsync(SessionParams parameters, CancellationToken cancellationToken)
    {
        return new RecordingStopResult(await recordings.StopAsync(parameters.SessionId, cancellationToken));
    }
}
