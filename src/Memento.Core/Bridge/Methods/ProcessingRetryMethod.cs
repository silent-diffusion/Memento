using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Processing;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>processing.retry</c>: queues a failed stage again with the chosen remedy (<c>cpu</c>, <c>model:whisper-small</c>, <c>retry</c>).</summary>
public sealed class ProcessingRetryMethod(ProcessingOrchestrator processing, IProjectStore store) : BridgeMethod<ProcessingStageParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.ProcessingRetry;

    public override JsonTypeInfo<ProcessingStageParams> ParamsTypeInfo => BridgeJsonContext.Default.ProcessingStageParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(ProcessingStageParams parameters, CancellationToken cancellationToken)
    {
        if (!store.Exists(parameters.RecordingId))
        {
            throw ProjectService.NotFound(parameters.RecordingId);
        }

        await processing.RetryAsync(parameters.RecordingId, parameters.Stage, parameters.RemedyId, cancellationToken);
        return new EmptyResult();
    }
}
