using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recording;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>recording.start</c>. Either every source starts or none does.</summary>
public sealed class RecordingStartMethod(RecordingCoordinator recordings) : BridgeMethod<RecordingStartParams, RecordingStartResult>
{
    public override string Name => BridgeMethodNames.RecordingStart;

    public override JsonTypeInfo<RecordingStartParams> ParamsTypeInfo => BridgeJsonContext.Default.RecordingStartParams;

    public override JsonTypeInfo<RecordingStartResult> ResultTypeInfo => BridgeJsonContext.Default.RecordingStartResult;

    public override async Task<RecordingStartResult> InvokeAsync(RecordingStartParams parameters, CancellationToken cancellationToken)
    {
        return await recordings.StartAsync(parameters, cancellationToken);
    }
}
