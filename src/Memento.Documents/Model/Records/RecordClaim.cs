using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Records;

/// <summary>
/// One claim the pipeline produced and checked: the text, the transcript segment and words it cites, the owner and due
/// date for action items, the verifier's verdict and reason, and whether the grounding validator kept it.
/// </summary>
public sealed record RecordClaim
{
    /// <summary><c>m07-c3</c>: the module id and a number.</summary>
    public string Id { get; init; } = string.Empty;

    public string ModuleId { get; init; } = string.Empty;

    /// <summary><c>decision</c>, <c>action</c>, <c>point</c>, <c>quote</c>, <c>agenda</c>, <c>purpose</c>, <c>when</c>, <c>question</c>.</summary>
    public string Kind { get; init; } = string.Empty;

    public string Text { get; init; } = string.Empty;

    public string? SegmentId { get; init; }

    /// <summary>Start of the cited segment, seconds.</summary>
    public double? T { get; init; }

    public string? Quote { get; init; }

    public string? Owner { get; init; }

    public string? Due { get; init; }

    /// <summary><c>supported</c>, <c>unsupported</c> or <c>notChecked</c>.</summary>
    public string Verdict { get; init; } = "notChecked";

    /// <summary>The verifier's one-sentence reason.</summary>
    public string? Reason { get; init; }

    /// <summary>The owner was checked separately: <c>supported</c>, <c>unsupported</c> or <c>notChecked</c>.</summary>
    public string? OwnerVerdict { get; init; }

    /// <summary>The due date was checked separately: <c>supported</c>, <c>unsupported</c> or <c>notChecked</c>.</summary>
    public string? DueVerdict { get; init; }

    public bool Kept { get; init; }

    /// <summary>Why it was dropped or changed ("the quote is not in the transcript", "deferred, not decided").</summary>
    public string? Note { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
