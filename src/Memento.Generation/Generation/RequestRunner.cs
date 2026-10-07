using Memento.AI;
using Memento.AI.Local;
using Memento.Documents.Model.Records;

namespace Memento.Generation.Generation;

/// <summary>
/// Sends a pass's requests: the local model runs every pass of a generation in order on one model load in one worker
/// process (<see cref="LocalAiProvider.OpenSessionAsync"/>, unloaded when the runner is disposed); a cloud provider
/// gets them one by one, a few at a time. Each request is recorded by purpose, hash, tokens and stop reason (never by
/// content).
/// </summary>
public sealed class RequestRunner(IAiProvider provider, int cloudParallelism = 3, Action<AiRequest, AiResponse>? observer = null) : IAsyncDisposable
{
    private readonly List<RecordRequest> _records = [];
    private long _modelLoadMs;
    private LocalAiSession? _session;

    public IReadOnlyList<RecordRequest> Records => _records;

    public long ModelLoadMs => _modelLoadMs;

    /// <param name="progress">Receives the number of requests finished.</param>
    public async Task<IReadOnlyList<AiResponse>> RunAsync(IReadOnlyList<AiRequest> requests, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return [];
        }

        IReadOnlyList<AiResponse> responses;
        if (provider is LocalAiProvider local)
        {
            var relay = progress is null ? null : new IndexRelay(progress);
            _session ??= await local.OpenSessionAsync(cancellationToken);
            responses = await _session.GenerateManyAsync(requests, relay, cancellationToken);
        }
        else
        {
            var results = new AiResponse[requests.Count];
            var done = 0;
            using var gate = new SemaphoreSlim(Math.Max(1, cloudParallelism));
            var tasks = requests.Select(async (request, i) =>
            {
                await gate.WaitAsync(cancellationToken);
                try
                {
                    results[i] = await provider.GenerateAsync(request, null, cancellationToken);
                    lock (results)
                    {
                        // Counted and reported under one lock, so the counts arrive in order.
                        progress?.Report(++done);
                    }
                }
                finally
                {
                    gate.Release();
                }
            }).ToList();
            await Task.WhenAll(tasks);
            responses = results;
        }

        for (var i = 0; i < responses.Count; i++)
        {
            var response = responses[i];
            observer?.Invoke(requests[i], response);
            _records.Add(new RecordRequest
            {
                Purpose = requests[i].Purpose,
                Hash = response.RequestHash,
                InputTokens = response.Usage.InputTokens,
                OutputTokens = response.Usage.OutputTokens,
                StopReason = response.ProviderStopReason ?? response.StopReason.ToString(),
            });
            if (response.Timings.ModelLoad is { } load)
            {
                _modelLoadMs += (long)load.TotalMilliseconds;
            }
        }

        return responses;
    }

    /// <summary>Unloads the local model, if a pass loaded it.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_session is { } session)
        {
            _session = null;
            await session.DisposeAsync();
        }
    }

    private sealed class IndexRelay(IProgress<int> target) : IProgress<AiProgress>
    {
        private int _last = -1;

        public void Report(AiProgress value)
        {
            if (value.Index is { } index && index > _last)
            {
                _last = index;
                target.Report(index);
            }
        }
    }
}
