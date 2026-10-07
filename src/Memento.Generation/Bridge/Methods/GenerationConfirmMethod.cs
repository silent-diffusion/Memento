using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>generation.confirm</c>.</summary>
public sealed class GenerationConfirmMethod(GenerationService generation) : M4Method<GenerationConfirmParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.GenerationConfirm;

    public override JsonTypeInfo<GenerationConfirmParams> ParamsTypeInfo => M4BridgeJsonContext.Default.GenerationConfirmParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M4BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(GenerationConfirmParams parameters, CancellationToken cancellationToken) =>
        Done(() => generation.Confirm(parameters.JobId, parameters.Approved));
}
