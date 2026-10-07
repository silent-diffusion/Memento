using System.Text.Json;

namespace Memento.AI.Http;

/// <summary>Small readers for provider JSON, and the shared rule for parsing a JSON answer.</summary>
internal static class CloudJson
{
    public static string? String(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static int? Int(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    /// <summary>
    /// The parsed answer when the request asked for JSON and generation completed; <c>null</c> for a truncated,
    /// refused or plain-text answer. A completed answer that is not JSON is a provider failure.
    /// </summary>
    public static JsonElement? ParseAnswer(AiRequest request, CloudStreamResult result, string provider)
    {
        if (!request.ExpectsJson || result.StopReason != AiStopReason.Completed)
        {
            return null;
        }

        return ParseJsonAnswer(result.Text) ?? throw new AiException(AiErrors.Unreadable(provider, "the answer is not valid JSON"));
    }

    /// <summary>Parses a whole-text JSON answer (surrounding whitespace allowed); <c>null</c> when it is not JSON.</summary>
    public static JsonElement? ParseJsonAnswer(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
