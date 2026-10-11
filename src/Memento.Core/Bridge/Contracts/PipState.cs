namespace Memento.Core.Bridge.Contracts;

/// <summary>The camera inset (picture-in-picture): drawn by the live preview and Review, burnt in only by the export's "One file".</summary>
/// <param name="Corner"><c>topLeft</c>, <c>topRight</c>, <c>bottomLeft</c> or <c>bottomRight</c>.</param>
public sealed record PipState(bool Enabled, string Corner);
