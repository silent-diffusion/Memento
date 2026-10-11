using System.Text.Json.Nodes;

namespace Memento.Core.Projects;

/// <summary>
/// <c>project.json</c> v5 → v6 (2.1): adds <c>segments</c>, the stretches "Resume recording" appends. A v5 recording was
/// recorded in one go, so it gets one segment starting at <c>createdAt</c> with the recording's duration (an import too,
/// which is never resumed). A <c>segments</c> field that is already there is kept as it is; one that is not an array is
/// replaced, so the project stays readable.
/// </summary>
public static class SegmentsMigration
{
    public const string Field = "segments";

    public static JsonObject Upgrade(JsonObject manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest.TryGetPropertyValue(Field, out var existing) && existing is JsonArray)
        {
            return manifest;
        }

        var created = manifest["createdAt"] as JsonValue;
        if (created is null || !created.TryGetValue<string>(out _))
        {
            manifest[Field] = new JsonArray();
            return manifest;
        }

        var duration = manifest["durationMs"] is JsonValue value
            && long.TryParse(value.ToJsonString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var ms)
                ? Math.Max(0, ms)
                : 0;
        manifest[Field] = new JsonArray(new JsonObject
        {
            ["index"] = 1,
            ["startedAt"] = created.DeepClone(),
            ["offsetMs"] = 0,
            ["durationMs"] = duration,
            ["gapMs"] = 0,
            ["endedAt"] = null,
        });
        return manifest;
    }
}
