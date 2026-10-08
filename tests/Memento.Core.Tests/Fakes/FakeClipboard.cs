using Memento.Core.Host;

namespace Memento.Core.Tests.Fakes;

/// <summary>A clipboard that keeps what was copied, or refuses like a clipboard another program holds open.</summary>
internal sealed class FakeClipboard : IClipboard
{
    public List<ClipboardContent> Copies { get; } = [];

    public ClipboardContent? Current => Copies.Count == 0 ? null : Copies[^1];

    /// <summary>When set, every copy fails with this reason and nothing is kept.</summary>
    public string? Refuse { get; set; }

    public Task SetAsync(ClipboardContent content, CancellationToken cancellationToken)
    {
        if (Refuse is { } reason)
        {
            return Task.FromException(new ClipboardUnavailableException(reason));
        }

        Copies.Add(content);
        return Task.CompletedTask;
    }
}
