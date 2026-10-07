using Memento.Core.Bridge;

namespace Memento.Generation.Bridge;

/// <summary>
/// The M4 bridge errors with their copy (DESIGN.md §17: name the thing, say what is safe, then the fix). Provider failures
/// keep the provider's own sentence and code (<c>ai.network</c>, <c>ai.notEnoughVram</c>…).
/// </summary>
public static class M4Errors
{
    public static BridgeException ProjectNotFound(string recordingId) => new(
        DomainErrorCodes.ProjectNotFound,
        "This recording is no longer in the library; it may have been deleted. Nothing was changed. Go back to the Library to see what is there.",
        recordingId);

    public static BridgeException Invalid(string message, string? detail = null) => new(BridgeErrorCodes.InvalidParams, message, detail);

    public static BridgeException AiDisabled(string provider) => new(
        DomainErrorCodes.AiDisabled,
        $"{provider} is an external service, and external AI is off, so nothing was sent and no document was changed. Turn on \"Allow external AI services\" in Settings › AI and privacy, or choose the local model.",
        provider);

    /// <param name="code">The specific reason (<c>ai.noKey</c>, <c>ai.modelNotInstalled</c>, <c>ai.notEnoughVram</c>), put in <c>detail</c>.</param>
    public static BridgeException ProviderNotReady(string code, string message) => new(DomainErrorCodes.AiProviderNotReady, message, code);

    public static BridgeException NoTranscript(string title) => new(
        DomainErrorCodes.GenerationNoTranscript,
        $"\"{title}\" has no transcript yet, so there is nothing to generate the document from. Nothing was sent. Wait until transcription finishes, or start it from the recording's processing card.");

    public static BridgeException TranscriptNotSent(string title) => new(
        DomainErrorCodes.GenerationNoTranscript,
        $"The transcript of \"{title}\" is not among the inputs, so the generated sections could not cite it. Nothing was sent. Tick Transcript under \"What the AI receives\", and allow it in Settings › AI and privacy for external services.");

    public static BridgeException Busy() => new(
        DomainErrorCodes.GenerationBusy,
        "Another document is being generated. Nothing was sent. Wait until it finishes, or cancel it, then try again.");

    public static BridgeException GenerationNotFound(string jobId) => new(
        DomainErrorCodes.GenerationNotFound,
        "That generation is no longer running; it may have finished or been cancelled. Nothing was changed.",
        jobId);

    public static BridgeException TemplateNotFound(string templateId) => new(
        DomainErrorCodes.TemplatesNotFound,
        $"There is no template with the id \"{templateId}\"; it may have been deleted. Nothing was changed. Choose a template from the list.",
        templateId);

    public static BridgeException TemplateBuiltIn(string name) => new(
        DomainErrorCodes.TemplatesBuiltIn,
        $"\"{name}\" is a built-in template, so it can't be deleted. It was left as it is; use Reset to undo your changes to it, or duplicate it.");

    public static BridgeException StyleNotFound(string styleId) => new(
        DomainErrorCodes.StylesNotFound,
        $"There is no style with the id \"{styleId}\"; it may have been deleted. Nothing was changed. Choose a style from the list.",
        styleId);

    public static BridgeException StyleBuiltIn(string name) => new(
        DomainErrorCodes.StylesBuiltIn,
        $"\"{name}\" is a preset, so it can't be deleted. It was left as it is; use Reset to undo your changes to it, or duplicate it.");

    public static BridgeException StyleInUse(string name, IReadOnlyList<string> templates) => new(
        DomainErrorCodes.StylesInUse,
        $"\"{name}\" is the default style of {(templates.Count == 1 ? "the template" : "the templates")} {string.Join(", ", templates.Select(t => "\"" + t + "\""))}, so it was not deleted. Choose another default style for {(templates.Count == 1 ? "it" : "them")} first.",
        string.Join(", ", templates));

    public static BridgeException DocumentNotFound(string documentId) => new(
        DomainErrorCodes.DocumentsNotFound,
        "That document is no longer in this recording; it may have been deleted. Nothing was changed. Reopen the recording to see its documents.",
        documentId);

    public static BridgeException UnsupportedEdit(string what) => new(
        DomainErrorCodes.DocumentsUnsupportedEdit,
        $"The edit could not be saved: {what}. The document is as it was before this edit. Undo the last change, or paste the text without formatting.",
        what);

    public static BridgeException VersionNotFound(string versionId) => new(
        DomainErrorCodes.DocumentsVersionNotFound,
        "That version of the document is no longer kept; versions are removed after the number of days set in Settings › History. Nothing was changed.",
        versionId);

    public static BridgeException ExportFailed(string message, string? detail = null) => new(DomainErrorCodes.DocumentsExportFailed, message, detail);
}
