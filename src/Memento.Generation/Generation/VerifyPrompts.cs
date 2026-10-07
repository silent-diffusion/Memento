using System.Globalization;
using System.Text;
using System.Text.Json;
using Memento.AI;

namespace Memento.Generation.Generation;

/// <summary>
/// The verify pass (ARCHITECTURE.md §8 step 4): each claim with only the transcript span it cites, asked "does this span
/// support the claim?", with names as the transcript writes them and an action item's owner and due date asked as
/// separate questions. The local model answers one question per request (the spike's verifier); a cloud model answers
/// a numbered batch in one request, which costs far fewer round trips and keeps every claim beside its own span.
/// </summary>
public static class VerifyPrompts
{
    private const string Rules = """
        A claim is supported only if the excerpt states it, including every person, date and number in the claim. A proposal, an opinion, or something that was postponed, parked or left undecided does not support a claim that it was decided or agreed. People are named as the transcript labels the speakers; a first name in the excerpt that clearly refers to the same person counts as that person. A task said by a speaker about themselves ("I'll do it") names that speaker as its owner.
        """;

    public static string System => "You check whether a short transcript excerpt supports a claim. Use only the excerpt. " + Rules + """

        Answer with JSON only: {"reason":"one short sentence","supported":true or false}
        """;

    public static string BatchSystem => "You check claims against short transcript excerpts. Each numbered item has its own excerpt and one claim; judge every item using only its own excerpt. " + Rules + """

        Answer with JSON only: {"verdicts":[{"item":1,"reason":"one short sentence","supported":true or false}, ...]} with one entry per item, in order.
        """;

    /// <summary>The questions for one claim: the claim, then its owner and due date when it has them.</summary>
    public static IEnumerable<VerifyQuestion> Questions(Claim claim, Func<int, string?> agendaItem)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(agendaItem);
        if (claim.Line is not { } line || claim.Kind == ClaimKinds.Quote)
        {
            yield break;
        }

        var statement = claim.Kind switch
        {
            ClaimKinds.Decision => "Decision: " + claim.Text,
            ClaimKinds.Action => "Action item: " + claim.Text,
            ClaimKinds.Agenda => $"This excerpt discusses the agenda item \"{agendaItem(claim.AgendaItem ?? 0)}\".",
            ClaimKinds.When => "The next meeting: " + claim.Text,
            ClaimKinds.NextAgenda => "A topic proposed for the next meeting: " + claim.Text,
            _ => claim.Text,
        };
        yield return new VerifyQuestion(claim, VerifyQuestion.ClaimField, statement, line);
        if (claim.Kind == ClaimKinds.Action && claim.Owner is { } owner)
        {
            yield return new VerifyQuestion(claim, VerifyQuestion.OwnerField, $"{owner} is the person who will do this task: {claim.Text}", line);
        }

        if (claim.Kind == ClaimKinds.Action && claim.Due is { } due)
        {
            yield return new VerifyQuestion(claim, VerifyQuestion.DueField, $"The deadline stated for this task is \"{due}\": {claim.Text}", line);
        }
    }

    public static AiRequest ForQuestion(VerifyQuestion question, string excerpt) =>
        AiRequest.Create("verify." + question.Field, System, $"Excerpt:\n{excerpt}\n\nClaim: {question.Statement}", 160) with
        {
            JsonSchema = VerdictSchema,
            SchemaName = "verdict",
            Temperature = 0,
        };

    public static AiRequest Batch(IReadOnlyList<(VerifyQuestion Question, string Excerpt)> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        var user = new StringBuilder();
        for (var i = 0; i < items.Count; i++)
        {
            user.Append(CultureInfo.InvariantCulture, $"<item number=\"{i + 1}\">\nExcerpt:\n{items[i].Excerpt}\n\nClaim: {items[i].Question.Statement}\n</item>\n\n");
        }

        return AiRequest.Create("verify.batch", BatchSystem, user.ToString().TrimEnd(), Math.Max(400, 90 * items.Count)) with
        {
            JsonSchema = BatchSchema,
            SchemaName = "verdicts",
            Temperature = 0,
        };
    }

    public static (bool? Supported, string? Reason) ParseSingle(JsonElement json) =>
        json.ValueKind == JsonValueKind.Object && json.TryGetProperty("supported", out var supported) && supported.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? (supported.GetBoolean(), json.TryGetProperty("reason", out var reason) && reason.ValueKind == JsonValueKind.String ? reason.GetString() : null)
            : (null, null);

    /// <summary>Item number (1-based) → verdict; items the answer leaves out stay unchecked.</summary>
    public static IReadOnlyDictionary<int, (bool Supported, string? Reason)> ParseBatch(JsonElement json)
    {
        var result = new Dictionary<int, (bool, string?)>();
        if (json.ValueKind != JsonValueKind.Object || !json.TryGetProperty("verdicts", out var verdicts) || verdicts.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in verdicts.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.Object && item.TryGetProperty("item", out var number) && number.TryGetInt32(out var n)
                && ParseSingle(item) is { Supported: { } supported } verdict)
            {
                result[n] = (supported, verdict.Reason);
            }
        }

        return result;
    }

    private static readonly JsonElement VerdictSchema = Parse("""
        {"type":"object","properties":{"reason":{"type":"string"},"supported":{"type":"boolean"}},"required":["reason","supported"],"additionalProperties":false}
        """);

    private static readonly JsonElement BatchSchema = Parse("""
        {"type":"object","properties":{"verdicts":{"type":"array","items":{"type":"object","properties":{"item":{"type":"integer"},"reason":{"type":"string"},"supported":{"type":"boolean"}},"required":["item","reason","supported"],"additionalProperties":false}}},"required":["verdicts"],"additionalProperties":false}
        """);

    private static JsonElement Parse(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
