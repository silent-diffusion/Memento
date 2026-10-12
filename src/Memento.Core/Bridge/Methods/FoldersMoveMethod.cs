using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>folders.move</c> (2.1): moves a Library folder, with what it holds, into another folder or the top level.</summary>
public sealed class FoldersMoveMethod(FolderService folders) : BridgeMethod<FolderMoveParams, FolderPathResult>
{
    public override string Name => BridgeMethodNames.FoldersMove;

    public override JsonTypeInfo<FolderMoveParams> ParamsTypeInfo => LibraryBridgeJsonContext.Default.FolderMoveParams;

    public override JsonTypeInfo<FolderPathResult> ResultTypeInfo => LibraryBridgeJsonContext.Default.FolderPathResult;

    public override async Task<FolderPathResult> InvokeAsync(FolderMoveParams parameters, CancellationToken cancellationToken) =>
        new(await folders.MoveAsync(parameters.Path, parameters.Parent, cancellationToken));
}
