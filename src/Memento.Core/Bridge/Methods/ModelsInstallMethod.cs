using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Models;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>models.install</c>: starts a verified download (one at a time); progress arrives as <c>models.progress</c>.</summary>
public sealed class ModelsInstallMethod(IModelManager models) : BridgeMethod<ModelIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.ModelsInstall;

    public override JsonTypeInfo<ModelIdParams> ParamsTypeInfo => BridgeJsonContext.Default.ModelIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(ModelIdParams parameters, CancellationToken cancellationToken)
    {
        await models.InstallAsync(parameters.ModelId, cancellationToken);
        return new EmptyResult();
    }
}
