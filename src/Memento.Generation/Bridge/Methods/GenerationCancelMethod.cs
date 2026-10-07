using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>generation.cancel</c>.</summary>
public sealed class GenerationCancelMethod(GenerationService generation) : M4Method<JobIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.GenerationCancel;

    public override JsonTypeInfo<JobIdParams> ParamsTypeInfo => M4BridgeJsonContext.Default.JobIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M4BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(JobIdParams parameters, CancellationToken cancellationToken) =>
        Done(() => generation.Cancel(parameters.JobId));
}
