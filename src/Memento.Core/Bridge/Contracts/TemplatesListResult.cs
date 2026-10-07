namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>templates.list</c>: built-ins first.</summary>
public sealed record TemplatesListResult(IReadOnlyList<Template> Templates);
