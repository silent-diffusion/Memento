using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Maintenance;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>library.rebuildIndex</c>: re-reads every project folder into <c>library.db</c>.</summary>
public sealed class LibraryRebuildIndexMethod(LibraryUsageService usage) : BridgeMethod<EmptyParams, LibraryRebuildIndexResult>
{
    public override string Name => BridgeMethodNames.LibraryRebuildIndex;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => M3BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<LibraryRebuildIndexResult> ResultTypeInfo => M3BridgeJsonContext.Default.LibraryRebuildIndexResult;

    public override async Task<LibraryRebuildIndexResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        new(await usage.RebuildIndexAsync(cancellationToken));
}
