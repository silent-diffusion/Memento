using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recovery;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>recovery.list</c>: recordings repaired at launch that the user has not dismissed.</summary>
public sealed class RecoveryListMethod(RecoveryService recovery) : BridgeMethod<EmptyParams, RecoveryListResult>
{
    public override string Name => BridgeMethodNames.RecoveryList;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<RecoveryListResult> ResultTypeInfo => BridgeJsonContext.Default.RecoveryListResult;

    public override async Task<RecoveryListResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        return new RecoveryListResult(await recovery.ListAsync(cancellationToken));
    }
}
