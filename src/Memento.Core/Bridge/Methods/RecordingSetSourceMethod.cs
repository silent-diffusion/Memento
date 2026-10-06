using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>recording.setSource</c>: starts or ends one track; the others are untouched.</summary>
public sealed class RecordingSetSourceMethod(RecordingCoordinator recordings) : BridgeMethod<RecordingSetSourceParams, TracksResult>
{
    public override string Name => BridgeMethodNames.RecordingSetSource;

    public override JsonTypeInfo<RecordingSetSourceParams> ParamsTypeInfo => BridgeJsonContext.Default.RecordingSetSourceParams;

    public override JsonTypeInfo<TracksResult> ResultTypeInfo => BridgeJsonContext.Default.TracksResult;

    public override async Task<TracksResult> InvokeAsync(RecordingSetSourceParams parameters, CancellationToken cancellationToken)
    {
        return new TracksResult(await recordings.SetSourceAsync(parameters.SessionId, parameters.SourceId, parameters.Enabled, cancellationToken));
    }
}
