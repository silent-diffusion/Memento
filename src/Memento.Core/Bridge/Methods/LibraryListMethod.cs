using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>library.list</c> → <see cref="LibraryListResult"/>. Recording and the project store arrive in M1,
/// so the library is always empty in this version.
/// </summary>
public sealed class LibraryListMethod : BridgeMethod<EmptyParams, LibraryListResult>
{
    public override string Name => BridgeMethodNames.LibraryList;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => BridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<LibraryListResult> ResultTypeInfo => BridgeJsonContext.Default.LibraryListResult;

    public override Task<LibraryListResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) =>
        Task.FromResult(new LibraryListResult([], 0));
}
