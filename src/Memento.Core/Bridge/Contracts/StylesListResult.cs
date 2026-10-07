namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>styles.list</c>: presets first.</summary>
public sealed record StylesListResult(IReadOnlyList<Style> Styles);
