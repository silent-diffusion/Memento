using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Models;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>models.remove</c>: refused with <c>models.inUse</c> while a stage is using the model.</summary>
public sealed class ModelsRemoveMethod(IModelManager models) : BridgeMethod<ModelIdParams, EmptyResult>
{
    public override string Name => BridgeMethodNames.ModelsRemove;

    public override JsonTypeInfo<ModelIdParams> ParamsTypeInfo => BridgeJsonContext.Default.ModelIdParams;

    public override JsonTypeInfo<EmptyResult> ResultTypeInfo => BridgeJsonContext.Default.EmptyResult;

    public override async Task<EmptyResult> InvokeAsync(ModelIdParams parameters, CancellationToken cancellationToken)
    {
        await models.RemoveAsync(parameters.ModelId, cancellationToken);
        return new EmptyResult();
    }
}
