using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>documents.makeTemplate</c>: a new template from the document's generation.</summary>
public sealed class DocumentsMakeTemplateMethod(DocumentService documents) : M4Method<DocumentNameParams, Template>
{
    public override string Name => BridgeMethodNames.DocumentsMakeTemplate;

    public override JsonTypeInfo<DocumentNameParams> ParamsTypeInfo => M4BridgeJsonContext.Default.DocumentNameParams;

    public override JsonTypeInfo<Template> ResultTypeInfo => M4BridgeJsonContext.Default.Template;

    public override Task<Template> InvokeAsync(DocumentNameParams parameters, CancellationToken cancellationToken) =>
        documents.MakeTemplateAsync(parameters.RecordingId, parameters.DocumentId, parameters.Name, cancellationToken);
}
