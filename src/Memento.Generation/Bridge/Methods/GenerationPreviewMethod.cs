using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>generation.preview</c>: exactly what would be sent. Nothing is sent.</summary>
public sealed class GenerationPreviewMethod(GenerationService generation) : M4Method<GenerationTemplateParams, GenerationPreviewResult>
{
    public override string Name => BridgeMethodNames.GenerationPreview;

    public override JsonTypeInfo<GenerationTemplateParams> ParamsTypeInfo => M4BridgeJsonContext.Default.GenerationTemplateParams;

    public override JsonTypeInfo<GenerationPreviewResult> ResultTypeInfo => M4BridgeJsonContext.Default.GenerationPreviewResult;

    public override Task<GenerationPreviewResult> InvokeAsync(GenerationTemplateParams parameters, CancellationToken cancellationToken) =>
        generation.PreviewAsync(parameters.RecordingId, Template(parameters.Template), cancellationToken);
}
