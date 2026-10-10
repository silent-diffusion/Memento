using System.Globalization;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;

namespace Memento.Core.Transcripts;

/// <summary>
/// <c>annotations.suggestChapters</c>, <c>annotations.dismissSuggestion</c> and <c>annotations.restoreSuggestion</c>
/// (2.0): <see cref="ChapterSuggester"/> on the current transcript and topics, less the suggestions near a chapter the
/// recording has (an accepted suggestion becomes one) or one the user dismissed. Reads only, except the dismissed times
/// kept in <c>annotations.json</c>.
/// </summary>
public sealed class ChapterSuggestionService(IProjectStore store, TranscriptStore transcripts)
{
    /// <summary>A suggestion this close to a chapter or a dismissed suggestion is not shown.</summary>
    public const long NearMs = 60_000;

    /// <summary>At most this many dismissed times are kept; the oldest go first.</summary>
    public const int MaxDismissed = 200;

    public async Task<IReadOnlyList<ChapterSuggestion>> SuggestAsync(string recordingId, CancellationToken cancellationToken)
    {
        var annotations = await AnnotationsAsync(recordingId, cancellationToken);
        return await SuggestAsync(recordingId, annotations, cancellationToken);
    }

    public async Task<IReadOnlyList<ChapterSuggestion>> DismissAsync(string recordingId, long atMs, CancellationToken cancellationToken)
    {
        Validate(atMs);
        var annotations = await UpdateAsync(
            recordingId,
            d => d.DismissedSuggestions.Contains(atMs) ? d : d with { DismissedSuggestions = d.DismissedSuggestions.Append(atMs).TakeLast(MaxDismissed).ToList() },
            cancellationToken);
        return await SuggestAsync(recordingId, annotations, cancellationToken);
    }

    public async Task<IReadOnlyList<ChapterSuggestion>> RestoreAsync(string recordingId, long atMs, CancellationToken cancellationToken)
    {
        Validate(atMs);
        var annotations = await UpdateAsync(
            recordingId,
            d => d.DismissedSuggestions.Contains(atMs) ? d with { DismissedSuggestions = d.DismissedSuggestions.Where(t => t != atMs).ToList() } : d,
            cancellationToken);
        return await SuggestAsync(recordingId, annotations, cancellationToken);
    }

    private async Task<IReadOnlyList<ChapterSuggestion>> SuggestAsync(string recordingId, AnnotationsDocument annotations, CancellationToken cancellationToken)
    {
        TranscriptDocument? transcript;
        try
        {
            transcript = await transcripts.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectSchemaException ex)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, ex.Message);
        }

        if (transcript is null || !transcript.Complete)
        {
            return [];
        }

        var taken = annotations.Chapters.Select(c => c.AtMs).Concat(annotations.DismissedSuggestions).ToList();
        return ChapterSuggester.Suggest(transcript.Segments, annotations.Topics.Select(t => t.Label).ToList())
            .Where(s => taken.All(t => Math.Abs(t - s.AtMs) >= NearMs))
            .Select(s => new ChapterSuggestion(string.Create(CultureInfo.InvariantCulture, $"sc{s.AtMs}"), s.AtMs, s.Title, s.Basis))
            .ToList();
    }

    private static void Validate(long atMs)
    {
        if (atMs < 0)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidParams, "A suggestion's time (atMs) cannot be negative.");
        }
    }

    private async Task<AnnotationsDocument> AnnotationsAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            await store.LoadAsync(recordingId, cancellationToken);
            return await store.LoadAnnotationsAsync(recordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw ProjectService.NotFound(recordingId);
        }
    }

    private async Task<AnnotationsDocument> UpdateAsync(string recordingId, Func<AnnotationsDocument, AnnotationsDocument> update, CancellationToken cancellationToken)
    {
        try
        {
            await store.LoadAsync(recordingId, cancellationToken);
            return await store.UpdateAnnotationsAsync(recordingId, update, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw ProjectService.NotFound(recordingId);
        }
        catch (ProjectSchemaException ex)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, ex.Message);
        }
    }
}
