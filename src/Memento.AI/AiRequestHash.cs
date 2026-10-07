using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Memento.AI;

/// <summary>
/// The SHA-256 of a request in a canonical form (provider, model, purpose, system, turns, limits, schema, grammar),
/// lower-case hex. Two identical requests hash the same on every PC, so the generation record can store the hash when
/// "keep a record" is off and still prove which payload produced a document.
/// </summary>
public static class AiRequestHash
{
    public static string Compute(AiRequest request, string providerId, string model)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("provider", providerId);
            writer.WriteString("model", model);
            writer.WriteString("purpose", request.Purpose);
            writer.WriteString("system", request.System);
            writer.WriteStartArray("messages");
            foreach (var message in request.Messages)
            {
                writer.WriteStartObject();
                writer.WriteString("role", message.Role == AiRole.User ? "user" : "assistant");
                writer.WriteString("content", message.Content);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteNumber("maxOutputTokens", request.MaxOutputTokens);
            if (request.Temperature is { } temperature)
            {
                writer.WriteString("temperature", temperature.ToString("R", CultureInfo.InvariantCulture));
            }

            if (request.JsonSchema is { } schema)
            {
                writer.WriteString("schemaName", request.SchemaName);
                writer.WritePropertyName("schema");
                schema.WriteTo(writer);
            }

            if (request.Grammar is { } grammar)
            {
                writer.WriteString("grammar", grammar);
            }

            writer.WriteEndObject();
        }

        return Hex(buffer.ToArray());
    }

    /// <summary>SHA-256 of UTF-8 text, lower-case hex.</summary>
    public static string OfText(string text) => Hex(Encoding.UTF8.GetBytes(text ?? string.Empty));

    private static string Hex(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
