namespace Memento.Generation.Generation;

/// <summary>
/// A candidate statement on its way through the pipeline: produced by the map pass with a citation (the line number
/// the model read and the words it quoted), moved by citation repair, merged by the reducer, checked by the verifier
/// (the claim, then owner and due date separately) and finally kept or dropped by the grounding validator.
/// </summary>
public sealed class Claim
{
    /// <summary>The map family that produced it (<see cref="ModuleTask.Family"/>).</summary>
    public required string Family { get; init; }

    public required string Kind { get; init; }

    public required string Text { get; set; }

    public string? Owner { get; set; }

    public string? Due { get; set; }

    /// <summary>The cited line (the payload's short id), or <c>null</c> when the citation was dropped.</summary>
    public int? Line { get; set; }

    public string? Quote { get; set; }

    /// <summary>The chunk it came from (0-based).</summary>
    public int Chunk { get; init; }

    /// <summary>Agenda claims: the 1-based agenda item.</summary>
    public int? AgendaItem { get; init; }

    /// <summary>Set by citation repair or the reducer ("citation moved from line 12 to 14").</summary>
    public List<string> Notes { get; } = [];

    public string Verdict { get; set; } = Verdicts.NotChecked;

    public string? Reason { get; set; }

    public string? OwnerVerdict { get; set; }

    public string? DueVerdict { get; set; }

    /// <summary>Kept in the document; set by the grounding validator.</summary>
    public bool Kept { get; set; }

    /// <summary>Why the validator dropped or changed it.</summary>
    public string? DropReason { get; set; }

    /// <summary>
    /// The copies the reducer merged into this claim. When this claim is not supported, they are verified in turn, so an
    /// earlier wrong copy ("ships on November 19") cannot hide a later right one ("ships on November 12").
    /// </summary>
    public List<Claim> Alternates { get; } = [];

    /// <summary>The record id, assigned per module when the document is written.</summary>
    public string Id { get; set; } = string.Empty;

    public Claim Copy()
    {
        var copy = new Claim
        {
            Family = Family,
            Kind = Kind,
            Text = Text,
            Owner = Owner,
            Due = Due,
            Line = Line,
            Quote = Quote,
            Chunk = Chunk,
            AgendaItem = AgendaItem,
            Verdict = Verdict,
            Reason = Reason,
            OwnerVerdict = OwnerVerdict,
            DueVerdict = DueVerdict,
            Kept = Kept,
            DropReason = DropReason,
        };
        copy.Notes.AddRange(Notes);
        return copy;
    }
}
