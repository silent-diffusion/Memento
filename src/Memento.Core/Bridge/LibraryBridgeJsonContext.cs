using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Bridge;

/// <summary>
/// Source-generated serialization for the 2.1 Library contracts (BRIDGE.md "2.1 — Folders, dates and Delete audio"),
/// with the same options as <see cref="BridgeJsonContext"/>: camelCase, unknown request fields rejected.
/// </summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(EmptyParams))]
[JsonSerializable(typeof(RecordingIdParams))]
[JsonSerializable(typeof(FoldersListResult))]
[JsonSerializable(typeof(FolderCreateParams))]
[JsonSerializable(typeof(FolderRenameParams))]
[JsonSerializable(typeof(FolderMoveParams))]
[JsonSerializable(typeof(FolderPathParams))]
[JsonSerializable(typeof(FolderPathResult))]
[JsonSerializable(typeof(FolderDeleteResult))]
[JsonSerializable(typeof(LibraryMoveToFolderParams))]
[JsonSerializable(typeof(LibraryMoveToFolderResult))]
[JsonSerializable(typeof(AudioRemoveResult))]
[JsonSerializable(typeof(AudioRemoveEstimateParams))]
[JsonSerializable(typeof(AudioRemoveEstimate))]
public sealed partial class LibraryBridgeJsonContext : JsonSerializerContext
{
}
