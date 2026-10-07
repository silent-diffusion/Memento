namespace Memento.AI.Local;

/// <summary>A connection to a worker: its stdin, its protocol output, and whatever must be disposed afterwards (the process).</summary>
public sealed class LocalLlmChannel(TextWriter toWorker, TextReader fromWorker, IAsyncDisposable? owner = null) : IAsyncDisposable
{
    public TextWriter ToWorker { get; } = toWorker;

    public TextReader FromWorker { get; } = fromWorker;

    public async ValueTask DisposeAsync()
    {
        if (owner is not null)
        {
            await owner.DisposeAsync();
        }
    }
}
