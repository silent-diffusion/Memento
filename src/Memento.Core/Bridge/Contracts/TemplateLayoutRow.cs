namespace Memento.Core.Bridge.Contracts;

/// <summary>A template row of one to three module cards (TypeScript <c>TemplateRow</c>).</summary>
public sealed record TemplateLayoutRow(IReadOnlyList<ModuleSettings> Modules)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    public IReadOnlyList<ModuleSettings> Modules { get; set; } = Modules ?? [];
}
