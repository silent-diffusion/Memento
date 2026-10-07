using Memento.AI.Payload;
using Memento.Core.Projects;
using Memento.Core.Transcripts;

namespace Memento.Generation.Documents;

/// <summary>
/// Everything a recording holds that a document can draw on, read once per generation or composition: the manifest
/// (details, participants, agenda, attachments), the transcript, the annotations, the text of attachments that have
/// text, and the recording's other documents as text. Audio and video are never part of it.
/// </summary>
public sealed record RecordingMaterial(
    string RecordingId,
    ProjectManifest Manifest,
    TranscriptDocument? Transcript,
    AnnotationsDocument Annotations,
    IReadOnlyList<PayloadAttachment> Attachments,
    IReadOnlyList<PayloadDocument> OtherDocuments)
{
    public ProjectDetails Details => Manifest.Details;

    public bool HasTranscript => Transcript is { Segments.Count: > 0 };

    /// <summary>Speaker id → name exactly as the transcript shows it.</summary>
    public IReadOnlyDictionary<string, string> SpeakerNames =>
        (Transcript?.Speakers ?? []).GroupBy(s => s.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);

    /// <summary>The material as the payload composer's inputs (it decides from the ticks what is sent).</summary>
    public PayloadInputs ToPayloadInputs(string? instructions)
    {
        var details = Details;
        var fields = new List<KeyValuePair<string, string>>();
        void Field(string label, string? value)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                fields.Add(new(label, value.Trim()));
            }
        }

        Field("Purpose", details.Purpose);
        Field("Platform", details.Platform);
        Field("Organization", details.Organization);
        var segments = Transcript?.Segments ?? [];
        return new PayloadInputs
        {
            Details = new PayloadDetails(
                details.Title,
                Manifest.CreatedAt,
                Manifest.DurationMs > 0 ? TimeSpan.FromMilliseconds(Manifest.DurationMs) : null,
                details.Type,
                string.IsNullOrWhiteSpace(details.Location) ? null : details.Location,
                string.IsNullOrWhiteSpace(details.Notes) ? null : details.Notes,
                fields),
            Participants = details.Participants.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => new PayloadParticipant(p.Trim())).ToList(),
            Speakers = (Transcript?.Speakers ?? []).Select(s => new PayloadSpeaker(s.Id, s.Name)).ToList(),
            Segments = segments.Select(s => new PayloadSegment(s.Id, s.Start, s.End, s.Speaker, s.Text)).ToList(),
            Chapters = Annotations.Chapters.OrderBy(c => c.AtMs).Select(c => new PayloadMarker(c.AtMs / 1000.0, c.Title)).ToList(),
            Agenda = details.Agenda.Items.Select((item, i) => new PayloadAgendaItem((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), item.Text)).ToList(),
            Highlights = Annotations.Highlights.OrderBy(h => h.AtMs).Select(h => new PayloadHighlight(
                h.AtMs / 1000.0,
                h.SegmentId is { } id && segments.FirstOrDefault(s => s.Id == id) is { } segment ? segment.Text : h.Note,
                null,
                h.SegmentId is not null && !string.IsNullOrWhiteSpace(h.Note) ? h.Note : null)).ToList(),
            Attachments = Attachments,
            PreviousDocuments = OtherDocuments,
            Instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions,
        };
    }
}
