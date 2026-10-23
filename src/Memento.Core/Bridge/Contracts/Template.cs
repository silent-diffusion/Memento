using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary>A document template (BRIDGE.md M4).</summary>
/// <param name="Id">Empty in <c>templates.save</c> for a new template.</param>
/// <param name="RecordingTypes">The recording types it is offered for; empty means all.</param>
/// <param name="ProviderId"><c>anthropic</c>, <c>openai</c>, <c>local</c>, or <c>null</c> for the Settings default.</param>
/// <param name="ModifiedAt">When it was last saved; <c>null</c> for a built-in that was never changed.</param>
public sealed record Template(
    string Id,
    string Name,
    bool BuiltIn,
    IReadOnlyList<string> RecordingTypes,
    IReadOnlyList<TemplateLayoutRow> Rows,
    InputSelection Inputs,
    [property: JsonPropertyOrder(4)] string? ProviderId,
    [property: JsonPropertyOrder(5)] string StyleId,
    TemplateOutputSettings Output,
    [property: JsonPropertyOrder(7)] DateTimeOffset? ModifiedAt)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    [JsonPropertyOrder(1)]
    public IReadOnlyList<string> RecordingTypes { get; set; } = RecordingTypes ?? [];

    [JsonPropertyOrder(2)]
    public IReadOnlyList<TemplateLayoutRow> Rows { get; set; } = Rows ?? [];

    /// <summary>A template the page sends without inputs gets <see cref="InputSelection.Default"/>.</summary>
    [JsonPropertyOrder(3)]
    public InputSelection Inputs { get; set; } = Inputs ?? InputSelection.Default;

    /// <summary>A template the page sends without output options exports nothing extra.</summary>
    [JsonPropertyOrder(6)]
    public TemplateOutputSettings Output { get; set; } = Output ?? TemplateOutputSettings.None;

    /// <summary>What the generated document is called: the meta line kind and the word in "Generate {documentKind}" ("Meeting minutes"). Empty in <c>templates.save</c> keeps the stored one (or the name).</summary>
    [JsonPropertyOrder(8)]
    public string DocumentKind { get; set; } = string.Empty;

    /// <summary>Instructions for the whole document; they never override the grounding rules.</summary>
    [JsonPropertyOrder(8)]
    public string? ProcessingInstructions { get; init; }

    /// <summary>A built-in the user changed (Reset is available).</summary>
    [JsonPropertyOrder(8)]
    public bool Customized { get; init; }
}
