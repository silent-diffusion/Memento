using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Processing;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>processing.cancel</c>: stops the stage, keeping what it made; <c>processing.retry</c> starts it again.</summary>
public sealed class ProcessingCancelMethod(ProcessingOrchestrator processing, IProjectStore store) : BridgeMethod<ProcessingStageParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.ProcessingCancel;

    public override JsonTypeInfo<ProcessingStageParams> ParamsTypeInfo => BridgeJsonContext.Default.ProcessingStageParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(ProcessingStageParams parameters, CancellationToken cancellationToken)
    {
        if (!store.Exists(parameters.RecordingId))
        {
            throw ProjectService.NotFound(parameters.RecordingId);
        }

        await processing.CancelAsync(parameters.RecordingId, parameters.Stage, cancellationToken);
        return new EmptyResult();
    }
}
