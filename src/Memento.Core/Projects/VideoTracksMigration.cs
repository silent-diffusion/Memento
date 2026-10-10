using System.Text.Json;
using System.Text.Json.Nodes;

namespace Memento.Core.Projects;

/// <summary>
/// <c>project.json</c> v3 → v4 (2.0): <c>videoTracks</c>, reserved since v1 and kept untouched by every 1.x build, becomes
/// the typed <see cref="ProjectManifest.VideoTracks"/>. A 1.x file holds an empty array. An entry that is not a readable
/// <see cref="VideoTrack"/> (no id, role, source or file, or not of kind <c>video</c>), or a <c>videoTracks</c> field that
/// is not an array, would make the whole project unreadable, so the step keeps every readable entry as it is and drops
/// the others, as <see cref="AttachmentsMigration"/> did for attachments. The files themselves are never touched.
/// </summary>
public static class VideoTracksMigration
{
    public const string Field = "videoTracks";

    public static JsonObject Upgrade(JsonObject manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (!manifest.TryGetPropertyValue(Field, out var node))
        {
            return manifest;
        }

        if (node is not JsonArray entries)
        {
            manifest[Field] = new JsonArray();
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

    internal static bool IsReadable(JsonNode? entry)
    {
        if (entry is not JsonObject)
        {
            return false;
        }

        try
        {
            var track = entry.Deserialize(ProjectJsonContext.Default.VideoTrack);
            return track is not null
                && track.Kind == VideoTrack.KindVideo
                && !string.IsNullOrWhiteSpace(track.Id)
                && track.Role is VideoTrack.RoleScreen or VideoTrack.RoleCamera
                && !string.IsNullOrWhiteSpace(track.SourceId)
                && !string.IsNullOrWhiteSpace(track.File)
                && track.Parts is not null;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
