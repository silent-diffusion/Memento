using System.Text.Json.Nodes;

namespace Memento.Core.Projects;

/// <summary>
/// <c>project.json</c> v6 → v7 (2.7.0): <c>details.whoSpoke.added</c>, when each name was added during the recording.
/// Names in a v6 file were given with no time, so it gets an empty list; a list already there is kept, and a value that
/// is not a list is replaced with an empty one, so the project stays readable. Everything else is left as it is.
/// </summary>
public static class SpeakerAddedMigration
{
    public const string Field = "added";

    public static JsonObject Upgrade(JsonObject manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest["details"] is JsonObject details && details[WhoSpokeMigration.Field] is JsonObject whoSpoke
            && !(whoSpoke.TryGetPropertyValue(Field, out var existing) && existing is JsonArray))
        {
            whoSpoke[Field] = new JsonArray();
        }

        return manifest;
    }
}
