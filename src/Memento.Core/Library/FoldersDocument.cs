using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Library;

/// <summary><c>folders.json</c> (2.1): the Library's folder paths. Unknown fields are kept.</summary>
public sealed record FoldersDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>Folder paths such as <c>Clients/Acme</c> (<see cref="FolderPaths"/>).</summary>
    public IReadOnlyList<string> Folders { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
