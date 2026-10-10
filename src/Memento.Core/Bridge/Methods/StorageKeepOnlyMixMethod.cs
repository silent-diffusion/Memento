using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Maintenance;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>storage.keepOnlyMix</c> (2.0): removes the separate track files of existing recordings, keeping each mix;
/// progress through <c>storage.reclaimProgress</c>. The page asks before it calls this (DESIGN.md §17: nothing
/// destructive without asking).
/// </summary>
public sealed class StorageKeepOnlyMixMethod(StorageReclaimService reclaim) : BridgeMethod<StorageKeepOnlyMixParams, JobIdResult>
{
    public override string Name => BridgeMethodNames.StorageKeepOnlyMix;

    public override JsonTypeInfo<StorageKeepOnlyMixParams> ParamsTypeInfo => LiveBridgeJsonContext.Default.StorageKeepOnlyMixParams;

    public override JsonTypeInfo<JobIdResult> ResultTypeInfo => LiveBridgeJsonContext.Default.JobIdResult;

    public override async Task<JobIdResult> InvokeAsync(StorageKeepOnlyMixParams parameters, CancellationToken cancellationToken) =>
        new(await reclaim.StartKeepOnlyMixAsync(parameters, cancellationToken));
}
