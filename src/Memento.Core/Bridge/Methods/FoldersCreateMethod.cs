using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>folders.create</c> (2.1): a new empty Library folder. Nothing on disk but <c>folders.json</c> changes.</summary>
public sealed class FoldersCreateMethod(FolderService folders) : BridgeMethod<FolderCreateParams, FolderPathResult>
{
    public override string Name => BridgeMethodNames.FoldersCreate;

    public override JsonTypeInfo<FolderCreateParams> ParamsTypeInfo => LibraryBridgeJsonContext.Default.FolderCreateParams;

    public override JsonTypeInfo<FolderPathResult> ResultTypeInfo => LibraryBridgeJsonContext.Default.FolderPathResult;

    public override async Task<FolderPathResult> InvokeAsync(FolderCreateParams parameters, CancellationToken cancellationToken) =>
        new(await folders.CreateAsync(parameters.Parent, parameters.Name, cancellationToken));
}
