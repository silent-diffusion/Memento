using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;
using Memento.Core.Projects;
using Memento.Core.Transcripts;

namespace Memento.Core.Export;

/// <summary>
/// <c>transcript.copy</c>: the readable transcript (<see cref="TranscriptText"/>, the same formatter as the export files) on
/// the Windows clipboard, all of it or the lines a filtered view in Review shows. Reads only.
/// </summary>
public sealed class TranscriptClipboard(ProjectService projects, TranscriptStore transcripts, IClipboard clipboard)
{
    /// <summary>More ids than any transcript has lines; a longer list is refused.</summary>
    public const int MaxSegmentIds = 100_000;

    public async Task<TranscriptCopyResult> CopyAsync(TranscriptCopyParams parameters, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parameters);
        if (parameters.Format is not (ExportRules.Text or ExportRules.Markdown))
        {
            throw Invalid($"format: '{parameters.Format}' cannot be copied. Choose text or markdown.");
        }

        if (TranscriptText.Validate(parameters.Options) is { } problem)
        {
            throw Invalid(problem);
        }

        if (parameters.SegmentIds is { Count: > MaxSegmentIds })
        {
            throw Invalid($"At most {MaxSegmentIds} lines can be named in one copy.");
        }

        if (parameters.SegmentIds is { Count: 0 })
        {
            throw Invalid("No lines were chosen, so there is nothing to copy. Show all lines, or choose a filter that shows some.");
        }

        var project = await projects.GetAsync(parameters.RecordingId, cancellationToken);
        TranscriptDocument? transcript;
        try
        {
            transcript = await transcripts.LoadAsync(parameters.RecordingId, cancellationToken);
        }
        catch (ProjectSchemaException ex)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, "The transcript file could not be read, so nothing was copied. The recording is unchanged.", ex.Message);
        }

        if (transcript is null)
        {
            throw new BridgeException(
                DomainErrorCodes.TranscriptNone,
                "This recording has no transcript yet, so there is nothing to copy. Nothing was changed. It appears when transcription finishes.",
                parameters.RecordingId);
        }

        HashSet<string>? only = null;
        if (parameters.SegmentIds is { } ids)
        {
            only = new HashSet<string>(ids, StringComparer.Ordinal);
            var known = transcript.Segments.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            if (only.FirstOrDefault(id => !known.Contains(id)) is { } missing)
            {
                throw new BridgeException(
                    DomainErrorCodes.TranscriptSegmentNotFound,
                    "A line in the view is not in the transcript any more; it may have been transcribed again. Nothing was copied. Reopen the transcript and copy again.",
                    missing);
            }
        }

        var text = TranscriptText.Format(parameters.Format, transcript, project.Summary, parameters.Options, only);
        var lines = TranscriptText.Selected(transcript, only).Count;
        var total = only is null ? lines : TranscriptText.Selected(transcript).Count;
        await ClipboardWrite.SetAsync(clipboard, new ClipboardContent(text), "the transcript", cancellationToken);
        return new TranscriptCopyResult(lines, total, text.Length);
    }

    private static BridgeException Invalid(string message) => new(BridgeErrorCodes.InvalidParams, message);
}
