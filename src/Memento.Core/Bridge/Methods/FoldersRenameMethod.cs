using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>folders.rename</c> (2.1): renames a Library folder; its recordings and folders follow (their manifests change, no file moves).</summary>
public sealed class FoldersRenameMethod(FolderService folders) : BridgeMethod<FolderRenameParams, FolderPathResult>
{
    public override string Name => BridgeMethodNames.FoldersRename;

    public override JsonTypeInfo<FolderRenameParams> ParamsTypeInfo => LibraryBridgeJsonContext.Default.FolderRenameParams;

    public override JsonTypeInfo<FolderPathResult> ResultTypeInfo => LibraryBridgeJsonContext.Default.FolderPathResult;

    public override async Task<FolderPathResult> InvokeAsync(FolderRenameParams parameters, CancellationToken cancellationToken) =>
        new(await folders.RenameAsync(parameters.Path, parameters.Name, cancellationToken));
}
