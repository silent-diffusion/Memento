using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Memento.Core.Voices;

/// <summary>
/// Reads <c>voices/known.json</c> of any version this Memento knows. A file without <c>schemaVersion</c> (written by a
/// 2.0 preview) is read as v1. Every readable voice is kept as it is; a voice that is damaged (not an object, no id,
/// name or voice model, a duplicate id) is dropped, and so is a sample without a usable signature or of another length
/// than the voice's first, so one damaged entry never costs the others. A file from a newer Memento is refused
/// (<see cref="KnownVoicesNewerException"/>) and left untouched, so a downgrade never rewrites it.
/// </summary>
public static class KnownVoicesMigration
{
    /// <summary>The longest name kept; longer ones are cut (the bridge refuses them on write).</summary>
    public const int MaxNameLength = 100;

    /// <exception cref="JsonException">The file is not a JSON object.</exception>
    /// <exception cref="KnownVoicesNewerException">The file was written by a newer Memento.</exception>
    public static KnownVoicesDocument Read(JsonNode? root, out int dropped)
    {
        dropped = 0;
        if (root is not JsonObject document)
        {
            throw new JsonException("known.json is not a JSON object.");
        }

        var version = document.TryGetPropertyValue("schemaVersion", out var versionNode) && versionNode is JsonValue value && value.TryGetValue<int>(out var number)
            ? number
            : KnownVoicesDocument.CurrentSchemaVersion;
        if (version > KnownVoicesDocument.CurrentSchemaVersion)
        {
            throw new KnownVoicesNewerException(version);
        }

        var voices = new List<KnownVoice>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        if (document.TryGetPropertyValue("voices", out var list) && list is JsonArray entries)
        {
            foreach (var entry in entries)
            {
                if (Readable(entry) is { } voice && ids.Add(voice.Id))
                {
                    var (kept, lost) = Repair(voice);
                    dropped += lost;
                    voices.Add(kept);
                }
                else
                {
                    dropped++;
                }
            }
        }

        // Fields this version does not know are kept for the round trip.
        var unknown = document
            .Where(p => p.Key is not "schemaVersion" and not "voices" && p.Value is not null)
            .ToDictionary(p => p.Key, p => JsonSerializer.SerializeToElement(p.Value!, KnownVoicesJsonContext.Default.JsonNode), StringComparer.Ordinal);
        return new KnownVoicesDocument { SchemaVersion = KnownVoicesDocument.CurrentSchemaVersion, Voices = voices, ExtensionData = unknown.Count == 0 ? null : unknown };
    }

    private static KnownVoice? Readable(JsonNode? entry)
    {
        if (entry is not JsonObject)
        {
            return null;
        }

        try
        {
            var voice = entry.Deserialize(KnownVoicesJsonContext.Default.KnownVoice);
            return voice is null || string.IsNullOrWhiteSpace(voice.Id) || string.IsNullOrWhiteSpace(voice.Name) || string.IsNullOrWhiteSpace(voice.EmbeddingModelId) ? null : voice;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null; // A required member came as null.
        }
    }

    /// <summary>Keeps the samples that can be compared, and caps the lists and the name.</summary>
    private static (KnownVoice Voice, int Dropped) Repair(KnownVoice voice)
    {
        var samples = (voice.Samples ?? []).Where(s => s is not null && s.Embedding is { Count: > 0 } && !string.IsNullOrEmpty(s.RecordingId)).ToList();
        var length = samples.FirstOrDefault()?.Embedding.Count ?? 0;
        var usable = samples.Where(s => s.Embedding.Count == length && s.Embedding.All(float.IsFinite)).TakeLast(KnownVoice.MaxSamples).ToList();
        var name = voice.Name.Trim();
        return (voice with
        {
            Name = name.Length > MaxNameLength ? name[..MaxNameLength] : name,
            Samples = usable,
            Recordings = (voice.Recordings ?? []).Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.Ordinal).TakeLast(KnownVoice.MaxRecordings).ToList(),
            DeclinedIn = (voice.DeclinedIn ?? []).Where(r => !string.IsNullOrEmpty(r)).Distinct(StringComparer.Ordinal).TakeLast(KnownVoice.MaxRecordings).ToList(),
        }, (voice.Samples?.Count ?? 0) - usable.Count);
    }

    /// <summary>"v" and ten hex digits.</summary>
    public static string NewId() =>
        "v" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(5)).ToLower(CultureInfo.InvariantCulture);
}
