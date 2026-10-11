using System.Text.Json.Nodes;
using Memento.Core.Library;

namespace Memento.Core.Projects;

/// <summary>
/// <c>project.json</c> v4 → v5 (2.1): adds the Library folder a recording is filed in (<c>folder</c>, a path such as
/// <c>Clients/Acme</c>; <c>null</c> for the root). Older files have none and stay at the root. A <c>folder</c> that is not
/// a valid folder path (a hand edit, a damaged file) would hide the recording in a folder nobody can open, so it is
/// set to the root; nothing on disk moves either way, since folders are data in the manifest.
/// </summary>
public static class FolderMigration
{
    public const string Field = "folder";

    public static JsonObject Upgrade(JsonObject manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!manifest.TryGetPropertyValue(Field, out var node) || node is null)
        {
            manifest[Field] = null;
            return manifest;
        }

        manifest[Field] = node is JsonValue value && value.TryGetValue<string>(out var text) && FolderPaths.TryNormalize(text, out var path)
            ? path
            : null;
        return manifest;
    }
}
