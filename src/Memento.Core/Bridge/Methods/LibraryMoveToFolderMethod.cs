using System.Text.Json.Serialization.Metadata;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Library;

namespace Memento.Core.Bridge.Methods;

/// <summary><c>library.moveToFolder</c> (2.1): files recordings in a Library folder or the root (<c>project.json</c>'s <c>folder</c>; no file moves).</summary>
public sealed class LibraryMoveToFolderMethod(FolderService folders) : BridgeMethod<LibraryMoveToFolderParams, LibraryMoveToFolderResult>
{
    public const int MaxRecordings = 1000;

    public override string Name => BridgeMethodNames.LibraryMoveToFolder;

    public override JsonTypeInfo<LibraryMoveToFolderParams> ParamsTypeInfo => LibraryBridgeJsonContext.Default.LibraryMoveToFolderParams;

    public override JsonTypeInfo<LibraryMoveToFolderResult> ResultTypeInfo => LibraryBridgeJsonContext.Default.LibraryMoveToFolderResult;

    public override async Task<LibraryMoveToFolderResult> InvokeAsync(LibraryMoveToFolderParams parameters, CancellationToken cancellationToken)
    {
        if (parameters.RecordingIds.Count is 0 or > MaxRecordings)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, $"Name 1 to {MaxRecordings} recordings to move.");
        }

        return new LibraryMoveToFolderResult(await folders.MoveRecordingsAsync(parameters.RecordingIds, parameters.Folder, cancellationToken));
    }
}
