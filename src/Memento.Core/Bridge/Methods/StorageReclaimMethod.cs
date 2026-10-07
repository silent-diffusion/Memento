using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Maintenance;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>storage.reclaim</c>: the optimize stage with the chosen options; progress through <c>storage.reclaimProgress</c>.</summary>
public sealed class StorageReclaimMethod(StorageReclaimService reclaim) : BridgeMethod<StorageReclaimParams, JobIdResult>
{
    public override string Name => BridgeMethodNames.StorageReclaim;

    public override JsonTypeInfo<StorageReclaimParams> ParamsTypeInfo => M3BridgeJsonContext.Default.StorageReclaimParams;

    public override JsonTypeInfo<JobIdResult> ResultTypeInfo => M3BridgeJsonContext.Default.JobIdResult;

    public override async Task<JobIdResult> InvokeAsync(StorageReclaimParams parameters, CancellationToken cancellationToken) =>
        new(await reclaim.StartAsync(parameters, cancellationToken));
}
