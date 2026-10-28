using System.Globalization;
using System.Text.Json.Nodes;

namespace Memento.Core.Projects;

/// <summary>
/// <c>project.json</c> v7 → v8 (2.8.0): every video track gets <c>stoppedAtMs</c>, where it stopped on the recording
/// timeline, so a screen added or removed while recording has its own time range. Before 2.8.0 a track stopped where it
/// ended early (<c>endedEarlyAtMs</c>) or at the end of its frames (<c>startOffsetMs + durationMs</c>). A value already
/// there is kept; an entry that is not an object, or a <c>videoTracks</c> that is not an array, is left as it is.
/// </summary>
public static class VideoRangesMigration
{
    public const string Field = "stoppedAtMs";

    public static JsonObject Upgrade(JsonObject manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest["videoTracks"] is not JsonArray tracks)
        {
            return manifest;
        }

        foreach (var track in tracks.OfType<JsonObject>())
        {
            if (track.TryGetPropertyValue(Field, out var existing) && existing is not null)
            {
                continue;
            }

            track[Field] = Long(track["endedEarlyAtMs"]) is { } ended
                ? ended
                : Math.Max(0, (Long(track["startOffsetMs"]) ?? 0) + (Long(track["durationMs"]) ?? 0));
        }

        return manifest;
    }

    private static long? Long(JsonNode? node) =>
        node is JsonValue value && long.TryParse(value.ToJsonString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;
}
