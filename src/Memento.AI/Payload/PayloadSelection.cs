using Memento.Core.Settings;

namespace Memento.AI.Payload;

/// <summary>The ticked inputs (Document builder checklist): exactly these become the payload.</summary>
public sealed record PayloadSelection
{
    public bool Transcript { get; init; }

    public bool Details { get; init; }

    public bool Participants { get; init; }

    public bool Agenda { get; init; }

    /// <summary>Chapters and topics, rendered as an outline of the transcript.</summary>
    public bool ChaptersAndTopics { get; init; }

    /// <summary>Highlights and user notes.</summary>
    public bool Highlights { get; init; }

    public bool Attachments { get; init; }

    public bool PreviousDocuments { get; init; }

    public bool Instructions { get; init; }

    public static PayloadSelection None { get; } = new();

    public static PayloadSelection Everything { get; } = new()
    {
        Transcript = true,
        Details = true,
        Participants = true,
        Agenda = true,
        ChaptersAndTopics = true,
        Highlights = true,
        Attachments = true,
        PreviousDocuments = true,
        Instructions = true,
    };

    /// <summary>
    /// The Settings › AI and privacy defaults: what external services may receive. Chapters and topics travel with the
    /// transcript, and the user's own instructions are always allowed; previous documents are opt-in per template.
    /// </summary>
    public static PayloadSelection FromShareSettings(AiShareSettings share)
    {
        ArgumentNullException.ThrowIfNull(share);
        return new PayloadSelection
        {
            Transcript = share.Transcript,
            Details = share.Details,
            Participants = share.Participants,
            Agenda = share.Agenda,
            ChaptersAndTopics = share.Transcript,
            Highlights = share.Highlights,
            Attachments = share.Attachments,
            PreviousDocuments = false,
            Instructions = true,
        };
    }

    /// <summary>Both selections allow the input: a template's ticks never exceed what Settings permits.</summary>
    public PayloadSelection Intersect(PayloadSelection other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return new PayloadSelection
        {
            Transcript = Transcript && other.Transcript,
            Details = Details && other.Details,
            Participants = Participants && other.Participants,
            Agenda = Agenda && other.Agenda,
            ChaptersAndTopics = ChaptersAndTopics && other.ChaptersAndTopics,
            Highlights = Highlights && other.Highlights,
            Attachments = Attachments && other.Attachments,
            PreviousDocuments = PreviousDocuments && other.PreviousDocuments,
            Instructions = Instructions && other.Instructions,
        };
    }
}
