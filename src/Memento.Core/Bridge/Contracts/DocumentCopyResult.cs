namespace Memento.Core.Bridge.Contracts;

/// <summary>Result of <c>documents.copy</c>.</summary>
/// <param name="Characters">The length of the Markdown text copied.</param>
/// <param name="Formatted">The formatted (HTML) copy was put beside the text, for Word and Outlook.</param>
public sealed record DocumentCopyResult(int Characters, bool Formatted);
