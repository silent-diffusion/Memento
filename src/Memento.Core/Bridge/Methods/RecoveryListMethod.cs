using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Recovery;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>recovery.list</c>: recordings repaired at launch that the user has not dismissed.</summary>
public sealed class RecoveryListMethod(RecoveryService recovery, Library.LibraryAvailability? availability = null) : BridgeMethod<EmptyParams, RecoveryListResult>
{
    public override string Name => BridgeMethodNames.RecoveryList;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<RecoveryListResult> ResultTypeInfo => BridgeJsonContext.Default.RecoveryListResult;

    public override async Task<RecoveryListResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken)
    {
        // A library that is not connected has nothing to offer yet; library.list says why.
        if (availability?.Unavailable is not null)
        {
            return new RecoveryListResult([]);
        }

        return new RecoveryListResult(await recovery.ListAsync(cancellationToken));
    }
}
