using Memento.AI;
using Memento.AI.Local;
using Memento.Documents.Model.Records;

namespace Memento.Generation.Generation;

/// <summary>
/// Sends a pass's requests: the local model runs every pass of a generation in order on one model load in one worker
/// process (<see cref="LocalAiProvider.OpenSessionAsync"/>, unloaded when the runner is disposed); a cloud provider
/// gets them one by one, a few at a time. Each request is recorded by purpose, hash, tokens and stop reason (never by
/// content). With an <see cref="GenerationOutputFeed"/>, each request, the local model's tokens as they arrive and each
/// reply also go to the Live output sheet.
/// </summary>
public sealed class RequestRunner(IAiProvider provider, int cloudParallelism = 3, Action<AiRequest, AiResponse>? observer = null, GenerationOutputFeed? output = null) : IAsyncDisposable
{
    private readonly List<RecordRequest> _records = [];
    private long _modelLoadMs;
    private LocalAiSession? _session;

    public IReadOnlyList<RecordRequest> Records => _records;

    public long ModelLoadMs => _modelLoadMs;

    /// <param name="progress">Receives the number of requests finished.</param>
    /// <param name="passes">What each request is, for the Live output (by default its purpose).</param>
    public async Task<IReadOnlyList<AiResponse>> RunAsync(IReadOnlyList<AiRequest> requests, IProgress<int>? progress, CancellationToken cancellationToken, IReadOnlyList<OutputPass>? passes = null)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return [];
        }

        OutputPass PassOf(int i) => passes is not null && i < passes.Count
            ? passes[i]
            : new OutputPass(requests[i].Purpose.StartsWith("verify", StringComparison.Ordinal) ? GenerationOutputFeed.VerifyStep : GenerationOutputFeed.MapStep, requests[i].Purpose);

        IReadOnlyList<AiResponse> responses;
        if (provider is LocalAiProvider local)
        {
            var relay = progress is null && output is null ? null : new LocalRelay(progress, output, requests, PassOf);
            _session ??= await local.OpenSessionAsync(cancellationToken);
            responses = await _session.GenerateManyAsync(requests, relay, cancellationToken);
            if (output is not null)
            {
                for (var i = 0; i < responses.Count; i++)
                {
                    // A pass the worker answered on its own already has its reply; this covers any it did not.
                    var id = relay?.PassId(i) ?? output.Request(PassOf(i), requests[i], streamed: true);
                    output.Reply(id, responses[i]);
                }
            }
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
                    var id = output?.Request(PassOf(i), request, streamed: false);
                    results[i] = await provider.GenerateAsync(request, null, cancellationToken);
                    if (id is not null)
                    {
                        output!.Reply(id, results[i]);
                    }

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

    /// <summary>
    /// The local model's progress for one batch: the number of requests finished (when a request starts, all before it
    /// are done), and for the Live output each request as the model starts reading it, its tokens and its answer.
    /// Runs on the worker's reader; it only queues.
    /// </summary>
    private sealed class LocalRelay(IProgress<int>? target, GenerationOutputFeed? output, IReadOnlyList<AiRequest> requests, Func<int, OutputPass> passOf) : IProgress<AiProgress>
    {
        private readonly string?[] _ids = new string?[requests.Count];
        private int _last = -1;

        public string? PassId(int index) => index >= 0 && index < _ids.Length ? _ids[index] : null;

        public void Report(AiProgress value)
        {
            if (value.Index is not { } index || index < 0 || index >= _ids.Length)
            {
                return;
            }

            if (index > _last)
            {
                _last = index;
                target?.Report(index);
            }

            if (output is null || value.Stage is AiProgressStage.Loading)
            {
                return;
            }

            var id = _ids[index] ??= output.Request(passOf(index), requests[index], streamed: true);
            switch (value.Stage)
            {
                case AiProgressStage.Generating when value.Delta is { } delta:
                    output.Tokens(id, delta, value.OutputTokens, value.Elapsed);
                    break;
                case AiProgressStage.Answered when value.Answer is { } answer:
                    output.Answered(id, value.OutputTokens, answer);
                    break;
                default:
                    break;
            }
        }
    }
}
