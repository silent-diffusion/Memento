namespace Memento.Core.Bridge.Contracts;

/// <summary>Payload of <c>export.progress</c>: at most four per second while running, and once at the end.</summary>
/// <param name="State"><c>running</c>, <c>done</c>, <c>failed</c> or <c>cancelled</c>.</param>
/// <param name="Message">For <c>failed</c>: what could not be written and why (DESIGN.md §17); otherwise <c>null</c> or a note.</param>
/// <param name="OutputFolder">The folder the files are written to (the subfolder when one was made).</param>
/// <param name="Files">Files written so far (with <c>manifest.json</c> once done).</param>
/// <param name="Bytes">Bytes written so far.</param>
public sealed record ExportProgressPayload(
    string JobId,
    string RecordingId,
    int Percent,
    string? CurrentFile,
    string State,
    string? Message,
    string? OutputFolder,
    int Files,
    long Bytes);
