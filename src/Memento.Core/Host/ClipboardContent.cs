namespace Memento.Core.Host;

/// <summary>What one copy puts on the clipboard.</summary>
/// <param name="Text">Unicode text; every program can paste it.</param>
/// <param name="Html">
/// An HTML page or fragment for programs that paste formatted text (Word, Outlook), or <c>null</c>. The clipboard
/// implementation wraps it in the Windows <c>HTML Format</c> (<see cref="ClipboardHtml"/>).
/// </param>
public sealed record ClipboardContent(string Text, string? Html = null);
