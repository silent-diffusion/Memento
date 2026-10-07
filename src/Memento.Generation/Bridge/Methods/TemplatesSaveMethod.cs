using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>templates.save</c>: a new id when <c>id</c> is empty; saving a built-in creates a copy.</summary>
public sealed class TemplatesSaveMethod(TemplateService templates) : M4Method<TemplateSaveParams, Template>
{
    public override string Name => BridgeMethodNames.TemplatesSave;

    public override JsonTypeInfo<TemplateSaveParams> ParamsTypeInfo => M4BridgeJsonContext.Default.TemplateSaveParams;

    public override JsonTypeInfo<Template> ResultTypeInfo => M4BridgeJsonContext.Default.Template;

    public override Task<Template> InvokeAsync(TemplateSaveParams parameters, CancellationToken cancellationToken) =>
        templates.SaveAsync(parameters.Template ?? throw M4Errors.Invalid("templates.save needs a template.", "template"), cancellationToken);
}
