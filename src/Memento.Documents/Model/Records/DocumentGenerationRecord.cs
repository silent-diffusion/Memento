using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Documents.Model.Records;

/// <summary>
/// How a generated document was made (ARCHITECTURE.md §8, "Record"), stored inside the document file: the template,
/// style, provider and model, when and how long, exactly what was sent (the included sections, size, SHA-256 of the
/// payload, and the payload itself when "keep a record" is on), the chunks, the request hashes, and every claim the
/// pipeline checked with its verdict. "How this was made" and the History tab read it.
/// </summary>
public sealed record DocumentGenerationRecord
{
    public string Id { get; init; } = string.Empty;

    public string TemplateId { get; init; } = string.Empty;

    public string TemplateName { get; init; } = string.Empty;

    public string StyleId { get; init; } = string.Empty;

    public string ProviderId { get; init; } = string.Empty;

    /// <summary>"Claude", "ChatGPT", "Local model".</summary>
    public string ProviderName { get; init; } = string.Empty;

    /// <summary>The model id the requests went to (cloud id or local catalog id).</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>"claude-opus-5-5", "Qwen3.5 4B · graphics card".</summary>
    public string ModelLabel { get; init; } = string.Empty;

    public DateTimeOffset StartedAt { get; init; }

    public long DurationMs { get; init; }

    public RecordInputs Inputs { get; init; } = new();

    /// <summary>The included sections as the preview names them ("Transcript (312 segments, 4 speakers)").</summary>
    public IReadOnlyList<string> Sent { get; init; } = [];

    /// <summary>What was left out and why ("Attachments: not ticked").</summary>
    public IReadOnlyList<string> NotSent { get; init; } = [];

    /// <summary>UTF-8 bytes of the payload text.</summary>
    public long Bytes { get; init; }

    /// <summary>Lower-case hex SHA-256 of the payload text.</summary>
    public string PayloadHash { get; init; } = string.Empty;

    /// <summary>The payload text, when "keep a record of what was sent" was on.</summary>
    public string? PayloadText { get; init; }

    /// <summary>The local model read it on this PC: nothing was sent. Audio and video are never sent in any case.</summary>
    public bool StayedOnPc { get; init; }

    public int Chunks { get; init; }

    public IReadOnlyList<RecordRequest> Requests { get; init; } = [];

    public RecordTimings Timings { get; init; } = new();

    public IReadOnlyList<RecordModule> Modules { get; init; } = [];

    public IReadOnlyList<RecordClaim> Claims { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
