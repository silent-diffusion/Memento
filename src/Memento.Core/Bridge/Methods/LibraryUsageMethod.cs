using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Maintenance;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>library.usage</c>: Settings › Storage and history › Usage.</summary>
public sealed class LibraryUsageMethod(LibraryUsageService usage) : BridgeMethod<EmptyParams, LibraryUsage>
{
    public override string Name => BridgeMethodNames.LibraryUsage;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => M3BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<LibraryUsage> ResultTypeInfo => M3BridgeJsonContext.Default.LibraryUsage;

    public override Task<LibraryUsage> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) => usage.UsageAsync(cancellationToken);
}
