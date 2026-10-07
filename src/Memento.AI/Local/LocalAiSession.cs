namespace Memento.AI.Local;

/// <summary>
/// The local model loaded once for a whole generation (<see cref="LocalAiProvider.OpenSessionAsync"/>): every pass
/// (map, verification, second votes) runs its requests here in order. Disposing unloads the model and lets go of the
/// graphics card.
/// </summary>
public sealed class LocalAiSession : IAsyncDisposable
{
    private readonly LocalAiProvider _provider;
    private readonly ILocalLlmSession _session;
    private bool _loaded;

    internal LocalAiSession(LocalAiProvider provider, ILocalLlmSession session)
    {
        _provider = provider;
        _session = session;
    }

    /// <summary>Runs the requests in order on the loaded model (the first batch also waits for the load).</summary>
    /// <exception cref="AiException">The model failed; the session is over.</exception>
    public async Task<IReadOnlyList<AiResponse>> GenerateManyAsync(IReadOnlyList<AiRequest> requests, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        var first = !_loaded;
        var responses = await _provider.RunOnSessionAsync(_session, requests, first, progress, cancellationToken);
        _loaded |= requests.Count > 0;
        return responses;
    }

    public ValueTask DisposeAsync() => _session.DisposeAsync();
}
