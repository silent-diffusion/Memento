namespace Memento.Generation.Generation;

/// <summary>The kinds of claim the map pass produces (<see cref="Claim.Kind"/>, <c>RecordClaim.Kind</c>).</summary>
public static class ClaimKinds
{
    public const string Decision = "decision";
    public const string Action = "action";
    public const string Point = "point";
    public const string Quote = "quote";
    public const string Agenda = "agenda";
    public const string When = "when";
    public const string NextAgenda = "nextAgenda";
}
