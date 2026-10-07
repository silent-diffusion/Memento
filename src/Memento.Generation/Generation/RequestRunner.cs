using Memento.AI;
using Memento.AI.Local;
using Memento.Documents.Model.Records;

namespace Memento.Generation.Generation;

/// <summary>
/// Sends a pass's requests: the local model runs them in order on one model load in one worker process
/// (<see cref="LocalAiProvider.GenerateManyAsync"/>); a cloud provider gets them one by one, a few at a time. Each request
/// is recorded by purpose, hash, tokens and stop reason (never by content).
/// </summary>
public sealed class RequestRunner(IAiProvider provider, int cloudParallelism = 3)
{
    private readonly List<RecordRequest> _records = [];
    private long _modelLoadMs;

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
            responses = await local.GenerateManyAsync(requests, relay, cancellationToken);
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
                    progress?.Report(Interlocked.Increment(ref done));
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
