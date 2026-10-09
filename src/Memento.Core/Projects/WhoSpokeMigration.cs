using System.Text.Json.Nodes;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Projects;

/// <summary>
/// <c>project.json</c> v2 → v3 (after 1.2.0): <c>details.whoSpoke</c>, the recording's own speaker count and names, becomes
/// a typed field. A v2 file has none and gets <c>{ "count": null, "names": [] }</c> (Settings decide, as before). A value
/// that could not be read as one (left by a hand edit or a build that wrote something else there) keeps what is readable:
/// a whole count from 1 to 20 and the names that are non-empty text, trimmed, at most 100 characters, the first 20
/// without repeats (ignoring case); everything else is dropped, so the project stays readable.
/// </summary>
public static class WhoSpokeMigration
{
    public const string Field = "whoSpoke";

    public static JsonObject Upgrade(JsonObject manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        if (manifest["details"] is not JsonObject details)
        {
            return manifest;
        }

        details.TryGetPropertyValue(Field, out var node);
        details[Field] = Normalize(node as JsonObject);
        return manifest;
    }

    private static JsonObject Normalize(JsonObject? value)
    {
        int? count = null;
        if (value?["count"] is JsonValue number && number.TryGetValue<int>(out var n) && n is >= 1 and <= WhoSpoke.MaxCount)
        {
            count = n;
        }

        var names = new JsonArray();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (value?["names"] is JsonArray list)
        {
            foreach (var item in list)
            {
                if (item is JsonValue text && text.TryGetValue<string>(out var raw)
                    && raw.Trim() is { Length: > 0 and <= WhoSpoke.MaxNameLength } name
                    && names.Count < WhoSpoke.MaxNames
                    && seen.Add(name))
                {
                    names.Add(name);
                }
            }
        }

        return new JsonObject { ["count"] = count, ["names"] = names };
    }
}
