using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>folders.list</c> (2.1): the Library's folder tree with recording counts. Reads only.</summary>
public sealed class FoldersListMethod(FolderService folders) : BridgeMethod<EmptyParams, FoldersListResult>
{
    public override string Name => BridgeMethodNames.FoldersList;

    public override JsonTypeInfo<EmptyParams> ParamsTypeInfo => LibraryBridgeJsonContext.Default.EmptyParams;

    public override JsonTypeInfo<FoldersListResult> ResultTypeInfo => LibraryBridgeJsonContext.Default.FoldersListResult;

    public override Task<FoldersListResult> InvokeAsync(EmptyParams parameters, CancellationToken cancellationToken) => folders.ListAsync(cancellationToken);
}
