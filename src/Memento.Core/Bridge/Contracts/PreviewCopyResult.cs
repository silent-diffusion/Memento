namespace Memento.Core.Bridge.Contracts;

/// <summary>2.0, <c>generation.copyPreview</c>: what went onto the clipboard (as text).</summary>
/// <param name="Characters">The length of the text copied.</param>
/// <param name="Passes">How many requests it holds.</param>
public sealed record PreviewCopyResult(int Characters, int Passes);
