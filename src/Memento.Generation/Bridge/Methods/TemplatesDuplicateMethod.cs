using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>templates.duplicate</c>.</summary>
public sealed class TemplatesDuplicateMethod(TemplateService templates) : M4Method<TemplateIdParams, Template>
{
    public override string Name => BridgeMethodNames.TemplatesDuplicate;

    public override JsonTypeInfo<TemplateIdParams> ParamsTypeInfo => M4BridgeJsonContext.Default.TemplateIdParams;

    public override JsonTypeInfo<Template> ResultTypeInfo => M4BridgeJsonContext.Default.Template;

    public override Task<Template> InvokeAsync(TemplateIdParams parameters, CancellationToken cancellationToken) =>
        templates.DuplicateAsync(parameters.TemplateId, cancellationToken);
}
