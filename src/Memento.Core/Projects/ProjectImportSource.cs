using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Projects;

/// <summary>
/// Where an imported recording came from (<c>library.importMedia</c>), so an import cut short by a crash can be offered
/// again ("Import again") from the same file. The original file is never copied or changed.
/// </summary>
public sealed record ProjectImportSource
{
    /// <summary>Full path of the file as it was chosen.</summary>
    public required string Path { get; init; }

    /// <summary>The file name, for messages when the path no longer exists.</summary>
    public required string Name { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
