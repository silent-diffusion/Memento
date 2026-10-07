namespace Memento.Core.Bridge.Contracts;

/// <summary>A template row of one to three module cards (TypeScript <c>TemplateRow</c>).</summary>
public sealed record TemplateLayoutRow(IReadOnlyList<ModuleSettings> Modules);
