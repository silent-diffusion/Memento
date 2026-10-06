using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>recording.current</c>: lets the UI rejoin an active session after a reload.</summary>
public sealed class RecordingCurrentMethod(RecordingCoordinator recordings) : BridgeMethod<EmptyParams, RecordingCurrentResult>
{
    public override string Name => BridgeMethodNames.RecordingCurrent;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<RecordingCurrentResult> ResultTypeInfo => BridgeJsonContext.Default.RecordingCurrentResult;

    public override Task<RecordingCurrentResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        return Task.FromResult(new RecordingCurrentResult(recordings.Current));
    }
}
