using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>templates.list</c>: built-ins first, then the user's templates by name.</summary>
public sealed class TemplatesListMethod(TemplateService templates) : M4Method<EmptyParams, TemplatesListResult>
{
    public override string Name => BridgeMethodNames.TemplatesList;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => M4BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<TemplatesListResult> ResultTypeInfo => M4BridgeJsonContext.Default.TemplatesListResult;

    public override Task<TemplatesListResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        Wrap(templates.ListAsync(cancellationToken), t => new TemplatesListResult(t));
}
