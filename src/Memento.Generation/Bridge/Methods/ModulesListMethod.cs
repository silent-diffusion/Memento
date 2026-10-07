using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Settings;
using Memento.Documents.Model.Modules;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Memento.Generation.Generation;

namespace Memento.Generation.Bridge.Methods;

/// <summary><c>modules.list</c>: the module catalog the Builder palette reads.</summary>
public sealed class ModulesListMethod(ModuleCatalog catalog) : M4Method<EmptyParams, ModulesListResult>
{
    public override string Name => BridgeMethodNames.ModulesList;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => M4BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<ModulesListResult> ResultTypeInfo => M4BridgeJsonContext.Default.ModulesListResult;

    public override Task<ModulesListResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        Task.FromResult(new ModulesListResult(catalog.All.Select(M4Mapping.ToInfo).ToList()));
}
