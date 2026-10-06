using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>library.list</c> → <see cref="LibraryListResult"/>: the recordings matching the search and type filter, sorted,
/// with totals that reflect the filter. Date grouping is the UI's job.
/// </summary>
public sealed class LibraryListMethod(ILibraryIndex index) : BridgeMethod<LibraryListParams, LibraryListResult>
{
    public override string Name => BridgeMethodNames.LibraryList;

    public override JsonTypeInfo<LibraryListParams> ParamsTypeInfo => BridgeJsonContext.Default.LibraryListParams;

    public override JsonTypeInfo<LibraryListResult> ResultTypeInfo => BridgeJsonContext.Default.LibraryListResult;

    public override async Task<LibraryListResult> InvokeAsync(LibraryListParams parameters, CancellationToken cancellationToken)
    {
        var sort = parameters.Sort ?? LibrarySort.Newest;
        if (!LibrarySort.IsValid(sort))
        {
            throw new BridgeException(
                BridgeErrorCodes.InvalidParams,
                $"Sort '{sort}' is not available. Choose {string.Join(", ", LibrarySort.All)}.");
        }

        if (parameters.Query is { Length: > FtsQuery.MaxLength })
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"A search can be at most {FtsQuery.MaxLength} characters.");
        }

        if (parameters.Type is { Length: > 64 })
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, "A recording type name can be at most 64 characters.");
        }

        var result = await index.QueryAsync(new LibraryQuery(parameters.Query, parameters.Type, sort), cancellationToken);
        return new LibraryListResult(result.Recordings, result.TotalDurationMs, result.TotalCount);
    }
}
