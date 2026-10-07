using System.Text.Json;
using System.Text.Json.Nodes;

namespace Memento.Core.Projects;

/// <summary>
/// <c>project.json</c> v1 → v2. M3 builds before 0.4.0 kept the <c>attachments</c> array as untyped extension data and
/// skipped entries they could not read; v2 reads it as <see cref="ProjectManifest.Attachments"/>, so an entry that is
/// not a valid <see cref="AttachmentRecord"/> (or an <c>attachments</c> field that is not an array) would make the whole
/// project unreadable. The step keeps every readable entry as it is and drops the others; the files themselves stay
/// in the project's <c>attachments</c> folder.
/// </summary>
public static class AttachmentsMigration
{
    public const string Field = "attachments";

    public static JsonObject Upgrade(JsonObject manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!manifest.TryGetPropertyValue(Field, out var node))
        {
            return manifest;
        }

        if (node is not JsonArray entries)
        {
            manifest.Remove(Field);
            return manifest;
        }

        for (var i = entries.Count - 1; i >= 0; i--)
        {
            if (!IsReadable(entries[i]))
            {
                entries.RemoveAt(i);
            }
        }

        return manifest;
    }

    private static bool IsReadable(JsonNode? entry)
    {
        if (entry is not JsonObject)
        {
            return false;
        }

        try
        {
            var record = entry.Deserialize(ProjectJsonContext.Default.AttachmentRecord);
            return record is not null && !string.IsNullOrWhiteSpace(record.Id) && !string.IsNullOrWhiteSpace(record.File);
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
