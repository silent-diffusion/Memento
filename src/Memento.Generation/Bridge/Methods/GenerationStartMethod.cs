using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>generation.start</c>; with "ask before every send" a cloud job waits for <c>generation.confirm</c>.</summary>
public sealed class GenerationStartMethod(GenerationService generation) : M4Method<GenerationStartParams, GenerationStartResult>
{
    public override string Name => BridgeMethodNames.GenerationStart;

    public override JsonTypeInfo<GenerationStartParams> ParamsTypeInfo => M4BridgeJsonContext.Default.GenerationStartParams;

    public override JsonTypeInfo<GenerationStartResult> ResultTypeInfo => M4BridgeJsonContext.Default.GenerationStartResult;

    public override Task<GenerationStartResult> InvokeAsync(GenerationStartParams parameters, CancellationToken cancellationToken) =>
        generation.StartAsync(parameters.RecordingId, Template(parameters.Template), parameters.DocumentId, cancellationToken);
}
