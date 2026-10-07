using System.Text.Json;

namespace Memento.AI;

/// <summary>
/// One generation request, the same for every provider. The text in it is exactly what is sent (cloud) or read by the
/// local model: build it with the payload composer from the ticked inputs only. Audio and video have no place here.
/// </summary>
public sealed record AiRequest
{
    /// <summary>
    /// A stable tag naming what the request is for (<c>map.actionItems</c>, <c>verify.claim</c>, <c>check</c>). Logged
    /// and recorded in the generation record; never content.
    /// </summary>
    public required string Purpose { get; init; }

    /// <summary>The system prompt (instructions); may be empty.</summary>
    public string System { get; init; } = string.Empty;

    /// <summary>The conversation, ending with a user turn.</summary>
    public required IReadOnlyList<AiMessage> Messages { get; init; }

    /// <summary>
    /// The most answer tokens wanted. Cloud reasoning models also spend tokens on thinking, so their providers add a
    /// configurable headroom on top (see <c>CloudHttpOptions.ThinkingHeadroomTokens</c>).
    /// </summary>
    public int MaxOutputTokens { get; init; } = 4096;

    /// <summary>
    /// Sampling temperature. The local provider honours it (default 0, greedy). Current cloud reasoning models reject
    /// sampling parameters, so cloud providers send it only when their options allow.
    /// </summary>
    public double? Temperature { get; init; }

    /// <summary>
    /// A JSON Schema the answer must match. Cloud providers pass it to the provider's structured-output feature; the
    /// local provider turns it into a GBNF grammar unless <see cref="Grammar"/> is also set. Objects should list every
    /// property in <c>required</c> and set <c>additionalProperties</c> to false (both cloud providers ask for that).
    /// </summary>
    public JsonElement? JsonSchema { get; init; }

    /// <summary>A short identifier for <see cref="JsonSchema"/> (OpenAI requires a name).</summary>
    public string SchemaName { get; init; } = "result";

    /// <summary>A GBNF grammar (root rule <c>root</c>) for the local provider; cloud providers use <see cref="JsonSchema"/>.</summary>
    public string? Grammar { get; init; }

    /// <summary>The answer is JSON: <see cref="AiResponse.Json"/> is parsed when generation completes.</summary>
    public bool ExpectsJson => JsonSchema is not null || Grammar is not null;

    /// <summary>A request with one user turn.</summary>
    public static AiRequest Create(string purpose, string system, string user, int maxOutputTokens = 4096) => new()
    {
        Purpose = purpose,
        System = system,
        Messages = [AiMessage.User(user)],
        MaxOutputTokens = maxOutputTokens,
    };

    /// <summary>Throws when the request cannot be sent to any provider.</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Purpose))
        {
            throw new ArgumentException("The request needs a purpose tag.", nameof(Purpose));
        }

        if (Messages is null || Messages.Count == 0 || Messages[^1].Role != AiRole.User)
        {
            throw new ArgumentException("The request needs at least one message and must end with a user message.", nameof(Messages));
        }

        if (MaxOutputTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxOutputTokens), MaxOutputTokens, "The output limit must be positive.");
        }

        if (JsonSchema is { ValueKind: not JsonValueKind.Object })
        {
            throw new ArgumentException("The JSON schema must be a JSON object.", nameof(JsonSchema));
        }
    }
}
