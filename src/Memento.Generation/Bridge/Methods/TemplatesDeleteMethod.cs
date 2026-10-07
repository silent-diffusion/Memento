using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>templates.delete</c>: refused for built-ins (<c>templates.builtIn</c>).</summary>
public sealed class TemplatesDeleteMethod(TemplateService templates) : M4Method<TemplateIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.TemplatesDelete;

    public override JsonTypeInfo<TemplateIdParams> ParamsTypeInfo => M4BridgeJsonContext.Default.TemplateIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => M4BridgeJsonContext.Default.EmptyResult;

    public override Task<EmptyResult> InvokeAsync(TemplateIdParams parameters, CancellationToken cancellationToken) =>
        Done(templates.DeleteAsync(parameters.TemplateId, cancellationToken));
}
