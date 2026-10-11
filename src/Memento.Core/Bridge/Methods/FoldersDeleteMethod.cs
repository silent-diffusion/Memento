using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;

namespace Memento.Core.Bridge.Methods;

/// <summary>
/// <c>folders.delete</c> (2.1, after the page's confirmation): removes a Library folder; the recordings and folders in it
/// move up into its parent. No recording is deleted.
/// </summary>
public sealed class FoldersDeleteMethod(FolderService folders) : BridgeMethod<FolderPathParams, FolderDeleteResult>
{
    public override string Name => BridgeMethodNames.FoldersDelete;

    public override JsonTypeInfo<FolderPathParams> ParamsTypeInfo => LibraryBridgeJsonContext.Default.FolderPathParams;

    public override JsonTypeInfo<FolderDeleteResult> ResultTypeInfo => LibraryBridgeJsonContext.Default.FolderDeleteResult;

    public override async Task<FolderDeleteResult> InvokeAsync(FolderPathParams parameters, CancellationToken cancellationToken)
    {
        var (recordings, moved) = await folders.DeleteAsync(parameters.Path, cancellationToken);
        return new FolderDeleteResult(recordings, moved);
    }
}
