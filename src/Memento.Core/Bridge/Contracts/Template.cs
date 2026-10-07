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
    string? ProviderId,
    string StyleId,
    TemplateOutputSettings Output,
    DateTimeOffset? ModifiedAt)
{
    /// <summary>What the generated document is called: the meta line kind and the word in "Generate {documentKind}" ("Meeting minutes"). Empty in <c>templates.save</c> keeps the stored one (or the name).</summary>
    public string DocumentKind { get; init; } = string.Empty;

    /// <summary>Instructions for the whole document; they never override the grounding rules.</summary>
    public string? ProcessingInstructions { get; init; }

    /// <summary>A built-in the user changed (Reset is available).</summary>
    public bool Customized { get; init; }
}
