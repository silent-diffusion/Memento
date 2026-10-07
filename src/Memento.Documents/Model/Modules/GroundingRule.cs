namespace Memento.Documents.Model.Modules;

/// <summary>A grounding rule (PRODUCT-SPEC "Grounded Generation") that the generation pipeline and the validator enforce for a module.</summary>
/// <param name="Id">Stable id, referenced by <see cref="ModuleDefinition.GroundingRules"/> (<c>actionItemRequiresCommitment</c>).</param>
/// <param name="Description">What the rule requires, in one sentence.</param>
public sealed record GroundingRule(string Id, string Description);
