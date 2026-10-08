namespace Memento.Core.Host;

/// <summary>The clipboard when the host has none (tests, tools): every copy is refused and nothing changes.</summary>
public sealed class UnavailableClipboard : IClipboard
{
    public Task SetAsync(ClipboardContent content, CancellationToken cancellationToken) =>
        Task.FromException(new ClipboardUnavailableException("This copy of Memento has no window to reach the clipboard."));
}
