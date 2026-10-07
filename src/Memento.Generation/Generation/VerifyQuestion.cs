namespace Memento.Generation.Generation;

/// <summary>One question the verifier answers: the claim itself, or (for an action item) its owner or its due date.</summary>
/// <param name="Field"><c>claim</c>, <c>owner</c> or <c>due</c>.</param>
public sealed record VerifyQuestion(Claim Claim, string Field, string Statement, int Line)
{
    public const string ClaimField = "claim";
    public const string OwnerField = "owner";
    public const string DueField = "due";
}
