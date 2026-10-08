namespace Memento.Core.Host;

/// <summary>
/// The Windows clipboard, written by the host (BRIDGE.md, "Clipboard and transcript text options"): the page never uses
/// the browser's clipboard, so a copy works from any screen and its text stays on this PC.
/// </summary>
public interface IClipboard
{
    /// <summary>Replaces the clipboard's content.</summary>
    /// <exception cref="ClipboardUnavailableException">Windows did not let Memento open the clipboard; nothing was copied.</exception>
    Task SetAsync(ClipboardContent content, CancellationToken cancellationToken);
}
