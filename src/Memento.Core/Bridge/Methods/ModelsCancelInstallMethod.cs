using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Models;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>models.cancelInstall</c>: stops the download and removes the partial file.</summary>
public sealed class ModelsCancelInstallMethod(IModelManager models) : BridgeMethod<ModelIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.ModelsCancelInstall;

    public override JsonTypeInfo<ModelIdParams> ParamsTypeInfo => BridgeJsonContext.Default.ModelIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(ModelIdParams parameters, CancellationToken cancellationToken)
    {
        await models.CancelInstallAsync(parameters.ModelId);
        return new EmptyResult();
    }
}
