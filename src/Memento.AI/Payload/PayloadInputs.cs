namespace Memento.AI.Payload;

/// <summary>
/// Everything a recording could contribute to an AI request, already as text. There is deliberately no field for
/// audio or video: they are never sent (an attachment whose media type is audio or video is listed as not sent).
/// </summary>
public sealed record PayloadInputs
{
    public PayloadDetails? Details { get; init; }

    public IReadOnlyList<PayloadParticipant> Participants { get; init; } = [];

    /// <summary>The identified speakers (transcript <c>speakers[]</c>): names exactly as in the transcript.</summary>
    public IReadOnlyList<PayloadSpeaker> Speakers { get; init; } = [];

    /// <summary>Transcript segments in time order.</summary>
    public IReadOnlyList<PayloadSegment> Segments { get; init; } = [];

    public IReadOnlyList<PayloadMarker> Chapters { get; init; } = [];

    public IReadOnlyList<PayloadMarker> Topics { get; init; } = [];

    public IReadOnlyList<PayloadAgendaItem> Agenda { get; init; } = [];

    public IReadOnlyList<PayloadHighlight> Highlights { get; init; } = [];

    /// <summary>User annotations and notes.</summary>
    public IReadOnlyList<PayloadNote> Notes { get; init; } = [];

    /// <summary>Imported documents and attachments, with their extracted text.</summary>
    public IReadOnlyList<PayloadAttachment> Attachments { get; init; } = [];

    public IReadOnlyList<PayloadDocument> PreviousDocuments { get; init; } = [];

    /// <summary>The user's instructions for this generation.</summary>
    public string? Instructions { get; init; }
}
