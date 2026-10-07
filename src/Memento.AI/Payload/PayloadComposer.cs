using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Memento.AI.Payload;

/// <summary>
/// Builds the payload from the ticked inputs only (ARCHITECTURE.md section 8, "Generation"). Each input becomes a
/// tagged section (<c>&lt;agenda&gt;…&lt;/agenda&gt;</c>) so instructions and data stay apart; the transcript is
/// rendered one line per segment with short ids instead of times. Nothing is shortened, reworded or redacted: an
/// input is either in the payload unchanged or listed in <see cref="ComposedPayload.Excluded"/> with the reason.
/// Audio and video are never included.
/// </summary>
public static partial class PayloadComposer
{
    private const string UnknownSpeaker = "Unknown speaker";

    public static ComposedPayload Compose(PayloadInputs inputs, PayloadSelection selection)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(selection);
        var sections = new List<PayloadSection>();
        var excluded = new List<PayloadExclusion>();
        var neutralised = 0;
        var lines = RenderLines(inputs.Segments, inputs.Speakers);
        var lineAt = (double seconds) => LineAt(lines, seconds);

        string Clean(string? text)
        {
            var (value, count) = Neutralise((text ?? string.Empty).Replace("\r\n", "\n", StringComparison.Ordinal).Trim());
            neutralised += count;
            return value;
        }

        void Add(bool ticked, bool present, PayloadSectionKind kind, string title, string summary, Func<string> render)
        {
            if (!present)
            {
                return;
            }

            if (!ticked)
            {
                excluded.Add(new PayloadExclusion(kind, title, "not ticked"));
                return;
            }

            sections.Add(new PayloadSection(kind, title, summary, render()));
        }

        Add(selection.Instructions, !string.IsNullOrWhiteSpace(inputs.Instructions), PayloadSectionKind.Instructions, "Your instructions", string.Empty,
            () => Tag("instructions", Clean(inputs.Instructions)));

        Add(selection.Details, inputs.Details is not null, PayloadSectionKind.Details, "Recording details", string.Empty,
            () => Tag("recording_details", RenderDetails(inputs.Details!, Clean)));

        Add(selection.Participants, inputs.Participants.Count > 0, PayloadSectionKind.Participants, "Participants", Count(inputs.Participants.Count, "person", "people"),
            () => Tag("participants", string.Join('\n', inputs.Participants.Select(p => "- " + Clean(p.Name) + (string.IsNullOrWhiteSpace(p.Role) ? string.Empty : " (" + Clean(p.Role) + ")")))));

        Add(selection.Agenda, inputs.Agenda.Count > 0, PayloadSectionKind.Agenda, "Agenda", Count(inputs.Agenda.Count, "item", "items"),
            () => Tag("agenda", string.Join('\n', inputs.Agenda.Select(a => Clean(a.Number) + ". " + Clean(a.Title) + (string.IsNullOrWhiteSpace(a.Notes) ? string.Empty : " — " + Clean(a.Notes))))));

        var outline = inputs.Chapters.Select(c => (c.Start, Kind: "Chapter", c.Title)).Concat(inputs.Topics.Select(t => (t.Start, Kind: "Topic", t.Title))).OrderBy(o => o.Start).ToList();
        Add(selection.ChaptersAndTopics, outline.Count > 0, PayloadSectionKind.Outline, "Chapters and topics", Count(outline.Count, "entry", "entries"),
            () => Tag("outline", string.Join('\n', outline.Select(o => $"- {o.Kind} from [{lineAt(o.Start)}]: {Clean(o.Title)}"))));

        Add(selection.Highlights, inputs.Highlights.Count > 0, PayloadSectionKind.Highlights, "Highlights", Count(inputs.Highlights.Count, "highlight", "highlights"),
            () => Tag("highlights", string.Join('\n', inputs.Highlights.OrderBy(h => h.Start).Select(h => $"- [{lineAt(h.Start)}] {Clean(h.Text)}" + (string.IsNullOrWhiteSpace(h.Note) ? string.Empty : " — note: " + Clean(h.Note))))));

        Add(selection.Highlights, inputs.Notes.Count > 0, PayloadSectionKind.Notes, "Notes", Count(inputs.Notes.Count, "note", "notes"),
            () => Tag("notes", string.Join('\n', inputs.Notes.Select(n => "- " + (n.At is { } at ? $"[{lineAt(at)}] " : string.Empty) + Clean(n.Text)))));

        var sendable = new List<PayloadAttachment>();
        foreach (var attachment in inputs.Attachments)
        {
            if (IsMedia(attachment))
            {
                excluded.Add(new PayloadExclusion(PayloadSectionKind.Attachments, $"Attachment \"{attachment.Name}\"", "audio and video are never sent"));
            }
            else if (string.IsNullOrWhiteSpace(attachment.Text))
            {
                excluded.Add(new PayloadExclusion(PayloadSectionKind.Attachments, $"Attachment \"{attachment.Name}\"", "it has no text"));
            }
            else
            {
                sendable.Add(attachment);
            }
        }

        Add(selection.Attachments, sendable.Count > 0, PayloadSectionKind.Attachments, "Attachments", Count(sendable.Count, "file", "files"),
            () => Tag("attachments", string.Join("\n\n", sendable.Select(a => $"<attachment name=\"{Attribute(a.Name)}\">\n{Clean(a.Text)}\n</attachment>"))));

        Add(selection.PreviousDocuments, inputs.PreviousDocuments.Count > 0, PayloadSectionKind.PreviousDocuments, "Previous documents", Count(inputs.PreviousDocuments.Count, "document", "documents"),
            () => Tag("previous_documents", string.Join("\n\n", inputs.PreviousDocuments.Select(d => $"<document title=\"{Attribute(d.Title)}\">\n{Clean(d.Text)}\n</document>"))));

        var cleanedLines = new List<TranscriptLine>(lines.Count);
        Add(selection.Transcript, lines.Count > 0, PayloadSectionKind.Transcript, "Transcript",
            Count(lines.Count, "segment", "segments") + ", " + Count(lines.Select(l => l.SpeakerId).Distinct(StringComparer.Ordinal).Count(), "speaker", "speakers"),
            () =>
            {
                cleanedLines.AddRange(lines.Select(l => l with { Speaker = Clean(l.Speaker), Text = Clean(l.Text) }));
                return Tag("transcript", string.Join('\n', cleanedLines.Select(l => l.Rendered)));
            });

        var text = string.Join("\n\n", sections.Select(s => s.Text));
        return new ComposedPayload(sections, excluded, cleanedLines, text, AiRequestHash.OfText(text), neutralised);
    }

    /// <summary>
    /// The text of one per-chunk request: optionally the context sections, then the chunk as a transcript section
    /// that names its part and time range.
    /// </summary>
    public static string RenderChunk(ComposedPayload payload, TranscriptChunk chunk, int chunkCount, bool includeContext)
    {
        ArgumentNullException.ThrowIfNull(payload);
        ArgumentNullException.ThrowIfNull(chunk);
        var transcript = string.Create(CultureInfo.InvariantCulture, $"<transcript part=\"{chunk.Index + 1} of {chunkCount}\" from=\"{Clock(chunk.Start)}\" to=\"{Clock(chunk.End)}\">\n{chunk.Text}\n</transcript>");
        var context = includeContext ? payload.ContextText : string.Empty;
        return context.Length == 0 ? transcript : context + "\n\n" + transcript;
    }

    /// <summary>The transcript as lines with 1-based short ids, in time order.</summary>
    public static IReadOnlyList<TranscriptLine> RenderLines(IReadOnlyList<PayloadSegment> segments, IReadOnlyList<PayloadSpeaker> speakers)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentNullException.ThrowIfNull(speakers);
        var names = speakers.GroupBy(s => s.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);
        return segments
            .OrderBy(s => s.Start)
            .Select((s, i) => new TranscriptLine(
                i + 1,
                s.Id,
                s.Start,
                s.End,
                s.SpeakerId,
                s.SpeakerId is { } id && names.TryGetValue(id, out var name) && !string.IsNullOrWhiteSpace(name) ? name.Trim() : UnknownSpeaker,
                s.Text.Replace("\r\n", " ", StringComparison.Ordinal).Replace('\n', ' ').Trim()))
            .ToList();
    }

    /// <summary>"1:02:03" or "12:34".</summary>
    public static string Clock(double seconds)
    {
        var span = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return span.TotalHours >= 1
            ? string.Create(CultureInfo.InvariantCulture, $"{(int)span.TotalHours}:{span.Minutes:00}:{span.Seconds:00}")
            : string.Create(CultureInfo.InvariantCulture, $"{span.Minutes:00}:{span.Seconds:00}");
    }

    private static string RenderDetails(PayloadDetails details, Func<string?, string> clean)
    {
        var rows = new List<string>();
        void Row(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                rows.Add(label + ": " + clean(value));
            }
        }

        Row("Title", details.Title);
        Row("Date", details.RecordedAt?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture));
        Row("Duration", details.Duration is { } d ? Clock(d.TotalSeconds) : null);
        Row("Type", details.Type);
        Row("Location", details.Location);
        foreach (var (label, value) in details.Fields ?? [])
        {
            Row(clean(label), value);
        }

        Row("Description", details.Description);
        return string.Join('\n', rows);
    }

    private static string Tag(string name, string body) => $"<{name}>\n{body}\n</{name}>";

    private static string Attribute(string value) => value.Replace("\"", "'", StringComparison.Ordinal).Replace('<', '‹').Replace('>', '›');

    private static string Count(int count, string one, string many) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {(count == 1 ? one : many)}");

    private static int LineAt(IReadOnlyList<TranscriptLine> lines, double seconds)
    {
        if (lines.Count == 0)
        {
            return 0;
        }

        var best = lines[0];
        foreach (var line in lines)
        {
            if (line.Start > seconds)
            {
                break;
            }

            best = line;
        }

        return best.ShortId;
    }

    private static bool IsMedia(PayloadAttachment attachment)
    {
        if (attachment.MediaType is { } type
            && (type.StartsWith("audio/", StringComparison.OrdinalIgnoreCase) || type.StartsWith("video/", StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var extension = Path.GetExtension(attachment.Name);
        return MediaExtensions.Contains(extension);
    }

    private static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".wav", ".flac", ".mp3", ".m4a", ".aac", ".ogg", ".opus", ".wma", ".aiff", ".mp4", ".m4v", ".mov", ".mkv", ".webm", ".avi", ".wmv",
    };

    private static (string Text, int Count) Neutralise(string text)
    {
        var count = 0;
        var result = SectionTag().Replace(text, match =>
        {
            count++;
            return "‹" + match.Value[1..];
        });
        return (result, count);
    }

    [GeneratedRegex(@"<\/?(?:instructions|recording_details|participants|agenda|outline|highlights|notes|attachments|attachment|previous_documents|document|transcript)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SectionTag();
}
