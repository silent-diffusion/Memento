using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Import;
using Memento.Core.Models;
using Memento.Core.Processing;
using Memento.Core.Projects;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>processing.retry</c>: queues a failed stage again with the chosen remedy (<c>cpu</c>, <c>model:whisper-small</c>,
/// <c>retry</c>), or downloads a damaged model again (<c>install:&lt;id&gt;</c>; the stage waits for it). On an import that was cut short, <c>stored</c> (remedy <c>importAgain</c> or none) imports the same
/// file again.
/// </summary>
public sealed class ProcessingRetryMethod(ProcessingOrchestrator processing, IProjectStore store, MediaImportService? imports = null, IModelManager? models = null) : BridgeMethod<ProcessingStageParams, EmptyResult>
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

        if (parameters.Stage == StageNames.Stored
            && imports is not null
            && parameters.RemedyId is null or MediaImportService.ImportAgainRemedy or Remedies.Retry)
        {
            await imports.ImportAgainAsync(parameters.RecordingId, cancellationToken);
            return new EmptyResult();
        }

        if (models is not null && parameters.RemedyId is { } remedy && remedy.StartsWith(Remedies.InstallPrefix, StringComparison.Ordinal))
        {
            // The stage waits for the model (cause noModel) and is queued again by itself once it is installed.
            await models.InstallAsync(remedy[Remedies.InstallPrefix.Length..], cancellationToken);
            return new EmptyResult();
        }

        await processing.RetryAsync(parameters.RecordingId, parameters.Stage, parameters.RemedyId, cancellationToken);
        return new EmptyResult();
    }
}
