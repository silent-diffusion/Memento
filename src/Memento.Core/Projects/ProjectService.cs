using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Library;
using Memento.Core.Processing;
using Memento.Core.Recording;
using Memento.Core.Transcripts;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Projects;

/// <summary>
/// Project operations behind the <c>project.*</c> and <c>annotations.*</c> bridge methods. Translates store
/// failures into the documented error codes with DESIGN.md §17 wording.
/// </summary>
public sealed partial class ProjectService(
    IProjectStore store,
    ProjectCatalog catalog,
    ILibraryIndex index,
    RecordingCoordinator recordings,
    ProcessingOrchestrator processing,
    TimeProvider time,
    ILogger<ProjectService> logger,
    TranscriptStore? transcripts = null)
{
    public const int MaxTitleLength = 200;
    public const int MaxTextLength = 4000;
    public const int MaxListItems = 200;

    private readonly ILogger<ProjectService> _logger = logger;

    public async Task<Project> GetAsync(string recordingId, CancellationToken cancellationToken)
    {
        var manifest = await LoadAsync(recordingId, cancellationToken);
        return await BuildAsync(manifest, cancellationToken);
    }

    public async Task<Project> UpdateDetailsAsync(string recordingId, RecordingDetailsPatch patch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var title = patch.Title is null ? null : ValidateTitle(patch.Title);
        var type = patch.Type is null ? null : ValidateType(patch.Type);
        var participants = patch.Participants is null ? null : ValidateList(patch.Participants, "participants");
        var tags = patch.Tags is null ? null : ValidateList(patch.Tags, "tags");
        var agenda = patch.Agenda is null ? null : ValidateAgenda(patch.Agenda);
        foreach (var (name, value) in new[] { ("purpose", patch.Purpose), ("platform", patch.Platform), ("organization", patch.Organization), ("location", patch.Location), ("notes", patch.Notes) })
        {
            ValidateText(value, name);
        }

        var saved = await UpdateAsync(
            recordingId,
            m => m with
            {
                Details = m.Details with
                {
                    Title = title ?? m.Details.Title,
                    Type = type ?? m.Details.Type,
                    Participants = participants ?? m.Details.Participants,
                    Purpose = patch.Purpose ?? m.Details.Purpose,
                    Platform = patch.Platform ?? m.Details.Platform,
                    Organization = patch.Organization ?? m.Details.Organization,
                    Location = patch.Location ?? m.Details.Location,
                    Notes = patch.Notes ?? m.Details.Notes,
                    Tags = tags ?? m.Details.Tags,
                    Agenda = agenda ?? m.Details.Agenda,
                },
            },
            cancellationToken);
        await AppendHistoryQuietlyAsync(recordingId, new HistoryEntry(time.GetLocalNow(), "edited", "info", "Details edited", null), cancellationToken);
        saved = await store.LoadAsync(recordingId, cancellationToken);
        await catalog.TouchedAsync(recordingId, cancellationToken);
        return await BuildAsync(saved, cancellationToken);
    }

    public async Task<Project> RenameAsync(string recordingId, string title, CancellationToken cancellationToken)
    {
        var value = ValidateTitle(title);
        var saved = await UpdateAsync(recordingId, m => m with { Details = m.Details with { Title = value } }, cancellationToken);
        await AppendHistoryQuietlyAsync(recordingId, new HistoryEntry(time.GetLocalNow(), "edited", "info", "Renamed", null), cancellationToken);
        await catalog.TouchedAsync(recordingId, cancellationToken);
        return await BuildAsync(saved, cancellationToken);
    }

    public async Task<ProjectDeleteEstimateResult> DeleteEstimateAsync(string recordingId, CancellationToken cancellationToken)
    {
        var manifest = await LoadAsync(recordingId, cancellationToken);
        var annotations = await LoadAnnotationsAsync(recordingId, cancellationToken);
        var folder = store.GetProjectFolder(recordingId);
        // Lower-case noun phrases the UI joins into one sentence (BRIDGE.md): "the recording, its 3 tracks and its transcript".
        var items = new List<string> { "the recording" };
        AddCount(items, manifest.Tracks.Count, "track", "tracks");
        if (File.Exists(Path.Combine(folder, ProjectLayout.TranscriptFile)))
        {
            items.Add("its transcript");
        }

        AddCount(items, annotations.Highlights.Count, "highlight", "highlights");
        AddCount(items, annotations.Chapters.Count, "chapter", "chapters");
        AddCount(items, CountFiles(Path.Combine(folder, ProjectLayout.AttachmentsFolder)), "attachment", "attachments");
        AddCount(items, CountFiles(Path.Combine(folder, ProjectLayout.DocumentsFolder)), "document", "documents");
        return new ProjectDeleteEstimateResult(manifest.Details.Title, store.GetSizeBytes(recordingId), items);
    }

    public async Task DeleteAsync(string recordingId, CancellationToken cancellationToken)
    {
        var manifest = await LoadAsync(recordingId, cancellationToken);
        if (recordings.IsBusy(recordingId))
        {
            throw Recording(manifest.Details.Title);
        }

        // A stage still converting this recording stops first and lets go of its files.
        await processing.CancelAsync(recordingId);
        try
        {
            await store.DeleteAsync(recordingId, cancellationToken);
        }
        catch (ProjectBusyException)
        {
            throw Recording(manifest.Details.Title);
        }

        await index.RemoveAsync(recordingId, cancellationToken);
        catalog.NotifyChanged(recordingId);
        LogDeleted(recordingId);
    }

    public async Task<IReadOnlyList<Chapter>> AddChapterAsync(string recordingId, ChapterPatch patch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var chapter = new Chapter(
            AnnotationIds.New('c'),
            ValidateAt(patch.AtMs ?? throw Invalid("A new chapter needs a time (atMs).")),
            ValidateLabel(patch.Title ?? string.Empty, "chapter title"),
            ValidateOrigin(patch.Origin));
        var doc = await UpdateAnnotationsAsync(recordingId, d => d with { Chapters = Sorted(d.Chapters.Append(chapter)) }, cancellationToken);
        return doc.Chapters;
    }

    public async Task<IReadOnlyList<Chapter>> UpdateChapterAsync(string recordingId, ChapterPatch patch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var id = patch.Id ?? throw Invalid("Say which chapter to change: the chapter needs its id.");
        var at = patch.AtMs is { } ms ? ValidateAt(ms) : (long?)null;
        var title = patch.Title is null ? null : ValidateLabel(patch.Title, "chapter title");
        var origin = patch.Origin is null ? null : ValidateOrigin(patch.Origin);
        var doc = await UpdateAnnotationsAsync(
            recordingId,
            d =>
            {
                var existing = d.Chapters.FirstOrDefault(c => c.Id == id) ?? throw MissingAnnotation("chapter", id);
                var changed = existing with { AtMs = at ?? existing.AtMs, Title = title ?? existing.Title, Origin = origin ?? existing.Origin };
                return d with { Chapters = Sorted(d.Chapters.Select(c => c.Id == id ? changed : c)) };
            },
            cancellationToken);
        return doc.Chapters;
    }

    public async Task<IReadOnlyList<Chapter>> RemoveChapterAsync(string recordingId, string chapterId, CancellationToken cancellationToken)
    {
        var doc = await UpdateAnnotationsAsync(
            recordingId,
            d => d.Chapters.Any(c => c.Id == chapterId)
                ? d with { Chapters = d.Chapters.Where(c => c.Id != chapterId).ToList() }
                : throw MissingAnnotation("chapter", chapterId),
            cancellationToken);
        return doc.Chapters;
    }

    public async Task<IReadOnlyList<Highlight>> AddHighlightAsync(string recordingId, HighlightPatch patch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var highlight = new Highlight(
            AnnotationIds.New('h'),
            ValidateAt(patch.AtMs ?? throw Invalid("A new highlight needs a time (atMs).")),
            ValidateNote(patch.Note),
            ValidateOrigin(patch.Origin),
            patch.SegmentId);
        highlight = highlight with { SegmentId = highlight.SegmentId ?? await SegmentAtAsync(recordingId, highlight.AtMs, cancellationToken) };
        var doc = await UpdateAnnotationsAsync(recordingId, d => d with { Highlights = Sorted(d.Highlights.Append(highlight)) }, cancellationToken);
        return doc.Highlights;
    }

    public async Task<IReadOnlyList<Highlight>> UpdateHighlightAsync(string recordingId, HighlightPatch patch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var id = patch.Id ?? throw Invalid("Say which highlight to change: the highlight needs its id.");
        var at = patch.AtMs is { } ms ? ValidateAt(ms) : (long?)null;
        var note = patch.Note is null ? null : ValidateNote(patch.Note);
        var origin = patch.Origin is null ? null : ValidateOrigin(patch.Origin);
        var moved = at is { } newAt ? await SegmentAtAsync(recordingId, newAt, cancellationToken) : null;
        var doc = await UpdateAnnotationsAsync(
            recordingId,
            d =>
            {
                var existing = d.Highlights.FirstOrDefault(h => h.Id == id) ?? throw MissingAnnotation("highlight", id);
                var changed = existing with
                {
                    AtMs = at ?? existing.AtMs,
                    Note = note ?? existing.Note,
                    Origin = origin ?? existing.Origin,
                    SegmentId = patch.SegmentId ?? moved ?? existing.SegmentId,
                };
                return d with { Highlights = Sorted(d.Highlights.Select(h => h.Id == id ? changed : h)) };
            },
            cancellationToken);
        return doc.Highlights;
    }

    public async Task<IReadOnlyList<Highlight>> RemoveHighlightAsync(string recordingId, string highlightId, CancellationToken cancellationToken)
    {
        var doc = await UpdateAnnotationsAsync(
            recordingId,
            d => d.Highlights.Any(h => h.Id == highlightId)
                ? d with { Highlights = d.Highlights.Where(h => h.Id != highlightId).ToList() }
                : throw MissingAnnotation("highlight", highlightId),
            cancellationToken);
        return doc.Highlights;
    }

    public async Task<IReadOnlyList<Topic>> AddTopicAsync(string recordingId, TopicPatch patch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(patch);
        var label = ValidateLabel(patch.Label ?? string.Empty, "topic");
        if (label.Length == 0)
        {
            throw Invalid("A topic needs a label.");
        }

        var topic = new Topic(AnnotationIds.New('t'), label, ValidateOrigin(patch.Origin));
        var doc = await UpdateAnnotationsAsync(
            recordingId,
            d => d.Topics.Any(t => string.Equals(t.Label, label, StringComparison.OrdinalIgnoreCase))
                ? d
                : d with { Topics = d.Topics.Append(topic).ToList() },
            cancellationToken);
        return doc.Topics;
    }

    public async Task<IReadOnlyList<Topic>> RemoveTopicAsync(string recordingId, string topicId, CancellationToken cancellationToken)
    {
        var doc = await UpdateAnnotationsAsync(
            recordingId,
            d => d.Topics.Any(t => t.Id == topicId)
                ? d with { Topics = d.Topics.Where(t => t.Id != topicId).ToList() }
                : throw MissingAnnotation("topic", topicId),
            cancellationToken);
        return doc.Topics;
    }

    internal static BridgeException NotFound(string recordingId) =>
        new(
            DomainErrorCodes.ProjectNotFound,
            "This recording is no longer in the library; it may have been deleted. Nothing was changed. Go back to the Library to see what is there.",
            recordingId);

    private static BridgeException Recording(string title) =>
        new(
            DomainErrorCodes.ProjectRecording,
            $"\"{title}\" is still recording or being saved, so it can't be deleted yet. Stop the recording and wait for it to finish saving, then delete it.");

    private static BridgeException Invalid(string message) => new(BridgeErrorCodes.InvalidParams, message);

    /// <summary><c>annotations.notFound</c>: the id names no chapter, highlight or topic of this recording.</summary>
    private static BridgeException MissingAnnotation(string kind, string id) =>
        new(
            DomainErrorCodes.AnnotationsNotFound,
            $"That {kind} is not in this recording any more; it may have been removed. Nothing was changed. Reopen the recording to see its current {kind}s.",
            id);

    private static List<T> Sorted<T>(IEnumerable<T> items)
        where T : class =>
        items.OrderBy(i => i switch { Chapter c => c.AtMs, Highlight h => h.AtMs, _ => 0 }).ToList();

    private static string ValidateTitle(string title)
    {
        var value = title.Trim();
        if (value.Length is 0 or > MaxTitleLength)
        {
            throw Invalid($"A title needs 1 to {MaxTitleLength} characters.");
        }

        return value;
    }

    private static string ValidateType(string type)
    {
        var value = type.Trim();
        if (value.Length is 0 or > 64)
        {
            throw Invalid("A recording type needs a name of 1 to 64 characters, such as \"meeting\".");
        }

        return value;
    }

    private static void ValidateText(string? value, string field)
    {
        if (value is not null && value.Length > MaxTextLength)
        {
            throw Invalid($"The {field} can be at most {MaxTextLength} characters; this one has {value.Length}.");
        }
    }

    private static List<string> ValidateList(IReadOnlyList<string> values, string field)
    {
        if (values.Count > MaxListItems)
        {
            throw Invalid($"The {field} list can have at most {MaxListItems} entries.");
        }

        return values
            .Select(v => (v ?? string.Empty).Trim())
            .Where(v => v.Length > 0)
            .Select(v => v.Length > MaxTitleLength ? throw Invalid($"Each of the {field} can be at most {MaxTitleLength} characters.") : v)
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private static Agenda ValidateAgenda(Agenda agenda)
    {
        var items = agenda.Items ?? [];
        if (items.Count > MaxListItems)
        {
            throw Invalid($"An agenda can have at most {MaxListItems} items.");
        }

        return agenda with
        {
            Items = items.Select(i => i with
            {
                Id = string.IsNullOrWhiteSpace(i.Id) ? AnnotationIds.New('a') : i.Id,
                Text = ValidateLabel(i.Text ?? string.Empty, "agenda item"),
            }).ToList(),
        };
    }

    private static long ValidateAt(long atMs) =>
        atMs is < 0 or > 7L * 24 * 3600 * 1000 ? throw Invalid("A time must be within the recording (0 or more milliseconds).") : atMs;

    private static string ValidateLabel(string value, string field)
    {
        var trimmed = value.Trim();
        return trimmed.Length > MaxTitleLength ? throw Invalid($"A {field} can be at most {MaxTitleLength} characters.") : trimmed;
    }

    private static string ValidateNote(string? note)
    {
        var value = (note ?? string.Empty).Trim();
        return value.Length > MaxTextLength ? throw Invalid($"A note can be at most {MaxTextLength} characters.") : value;
    }

    private static string ValidateOrigin(string? origin) =>
        origin is null ? AnnotationOrigins.User
        : AnnotationOrigins.IsValid(origin) ? origin
        : throw Invalid($"Origin '{origin}' is not one of user, local, ai.");

    private static void AddCount(List<string> items, int count, string singular, string plural)
    {
        if (count > 0)
        {
            items.Add("its " + (count == 1 ? singular : HumanFormat.Count(count, singular, plural)));
        }
    }

    private static int CountFiles(string folder) =>
        Directory.Exists(folder) ? Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories).Count() : 0;

    private async Task<ProjectManifest> LoadAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            return await store.LoadAsync(recordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw NotFound(recordingId);
        }
    }

    private async Task<AnnotationsDocument> LoadAnnotationsAsync(string recordingId, CancellationToken cancellationToken)
    {
        try
        {
            return await store.LoadAnnotationsAsync(recordingId, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw NotFound(recordingId);
        }
    }

    private async Task<ProjectManifest> UpdateAsync(string recordingId, Func<ProjectManifest, ProjectManifest> update, CancellationToken cancellationToken)
    {
        try
        {
            return await catalog.UpdateAsync(recordingId, update, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw NotFound(recordingId);
        }
        catch (ProjectSchemaException ex)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, ex.Message);
        }
    }

    private async Task<AnnotationsDocument> UpdateAnnotationsAsync(string recordingId, Func<AnnotationsDocument, AnnotationsDocument> update, CancellationToken cancellationToken)
    {
        AnnotationsDocument doc;
        try
        {
            doc = await store.UpdateAnnotationsAsync(recordingId, update, cancellationToken);
        }
        catch (ProjectNotFoundException)
        {
            throw NotFound(recordingId);
        }
        catch (ProjectSchemaException ex)
        {
            throw new BridgeException(BridgeErrorCodes.InvalidRequest, ex.Message);
        }

        await catalog.TouchedAsync(recordingId, cancellationToken);
        return doc;
    }

    /// <summary>Once there is a transcript, a highlight points at the line at its time (BRIDGE.md M2 clarification 5).</summary>
    private async Task<string?> SegmentAtAsync(string recordingId, long atMs, CancellationToken cancellationToken)
    {
        if (transcripts is null || !ProjectId.IsValid(recordingId))
        {
            return null;
        }

        try
        {
            return await transcripts.LoadAsync(recordingId, cancellationToken) is { } transcript
                ? HighlightSegments.SegmentAt(transcript.Segments, atMs / 1000.0)
                : null;
        }
        catch (ProjectSchemaException)
        {
            return null;
        }
    }

    private async Task AppendHistoryQuietlyAsync(string recordingId, HistoryEntry entry, CancellationToken cancellationToken)
    {
        try
        {
            await store.AppendHistoryAsync(recordingId, entry, cancellationToken);
        }
        catch (IOException ex)
        {
            LogHistoryFailed(ex, recordingId);
        }
    }

    private async Task<Project> BuildAsync(ProjectManifest manifest, CancellationToken cancellationToken)
    {
        var annotations = await LoadAnnotationsAsync(manifest.Id, cancellationToken);
        var history = await store.ReadHistoryAsync(manifest.Id, cancellationToken);
        var finalized = manifest.State is ProjectStates.Ready or ProjectStates.Recovered;
        var mixUrl = finalized && manifest.Mix is { } mix ? LibraryUrls.ForProjectFile(manifest.Id, mix.File) : null;
        var peaksUrl = finalized && manifest.Peaks is { } peaks ? LibraryUrls.ForProjectFile(manifest.Id, peaks) : null;
        return new Project(
            ProjectMapper.ToSummary(manifest, store.GetSizeBytes(manifest.Id)),
            ProjectMapper.ToDetails(manifest.Details),
            manifest.Tracks.Select(ProjectMapper.ToTrack).ToList(),
            mixUrl,
            peaksUrl,
            annotations.Chapters,
            annotations.Highlights,
            annotations.Topics,
            history,
            new IntegrityInfo(manifest.Integrity.Algorithm, manifest.Integrity.ComputedAt),
            store.GetSizeBytes(manifest.Id));
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Recording {RecordingId} deleted through the Delete flow")]
    private partial void LogDeleted(string recordingId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "History of recording {RecordingId} could not be appended")]
    private partial void LogHistoryFailed(Exception exception, string recordingId);
}
