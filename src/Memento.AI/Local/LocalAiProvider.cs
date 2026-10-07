using System.Diagnostics;
using System.Globalization;
using Memento.AI.Http;

namespace Memento.AI.Local;

/// <summary>
/// <see cref="IAiProvider"/> for an on-device model. Each call is one <see cref="LocalLlmJob"/> run by an
/// <see cref="ILocalLlmJobClient"/> (in production: Memento.Worker, so a native failure cannot take the app down and
/// video memory is released afterwards). <see cref="GenerateManyAsync"/> runs several requests on one model load.
/// JSON schemas become GBNF grammars; nothing leaves the PC.
/// </summary>
public sealed class LocalAiProvider : IAiProvider
{
    public const string ProviderId = "local";
    public const string ProviderName = "Local model";

    private readonly LocalModelEntry _model;
    private readonly string _modelPath;
    private readonly ILocalLlmJobClient _client;
    private readonly ITokenCounter _counter;
    private readonly Func<long?> _freeVram;
    private readonly LocalAiOptions _options;

    /// <param name="counter">The model's tokenizer (Memento.AI.Local's <c>LlamaTokenCounter</c>), or an estimate until it is available.</param>
    /// <param name="freeVram">Free video memory on the graphics card the worker will use (Core's resource probe), or <c>null</c>.</param>
    public LocalAiProvider(LocalModelEntry model, string modelPath, ILocalLlmJobClient client, ITokenCounter counter, Func<long?> freeVram, LocalAiOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(modelPath);
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(counter);
        ArgumentNullException.ThrowIfNull(freeVram);
        _model = model;
        _modelPath = modelPath;
        _client = client;
        _counter = counter;
        _freeVram = freeVram;
        _options = options ?? new LocalAiOptions();
        var context = _options.ContextTokens > 0 ? _options.ContextTokens : model.Llm.ContextTokens;
        Capabilities = new AiCapabilities(context, context / 2, SupportsJsonSchema: true, SupportsGrammar: true, SupportsStreaming: true, ExactTokenCounts: counter.IsExact);
    }

    public string Id => ProviderId;

    public string DisplayName => ProviderName;

    public AiProviderKind Kind => AiProviderKind.Local;

    public string Model => _model.Id;

    /// <summary>The largest context the model is configured for; <see cref="Plan"/> says what fits right now.</summary>
    public AiCapabilities Capabilities { get; }

    public int CountTokens(string text) => _counter.Count(text);

    /// <summary>How the model would be loaded with the free video memory measured now.</summary>
    public LocalLlmPlan Plan() =>
        LocalVramPlanner.Plan(_model.Llm, _options.Device, _freeVram(), _options.ContextTokens, _options.VramMarginBytes);

    public Task<AiReadiness> CheckAsync(CancellationToken cancellationToken)
    {
        var file = new FileInfo(_modelPath);
        if (!file.Exists || (_model.SizeBytes > 0 && file.Length != _model.SizeBytes))
        {
            return Task.FromResult(AiReadiness.NotReady(AiErrors.ModelNotInstalled(DisplayName, _model.Name)));
        }

        var plan = Plan();
        if (!plan.Fits)
        {
            return Task.FromResult(AiReadiness.NotReady(AiErrors.NotEnoughVram(DisplayName, _model.Name, _freeVram() ?? 0, plan.NeededVramBytes), plan.Reason));
        }

        var where = plan.UseGpu
            ? string.Create(CultureInfo.InvariantCulture, $"runs on the graphics card with a {plan.ContextTokens / 1024}k context")
            : string.Create(CultureInfo.InvariantCulture, $"runs on the processor with a {plan.ContextTokens / 1024}k context");
        return Task.FromResult(AiReadiness.Ready($"{_model.Name} {where} ({plan.Reason})"));
    }

    public async Task<AiResponse> GenerateAsync(AiRequest request, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var responses = await GenerateManyAsync([request], progress, cancellationToken);
        return responses[0];
    }

    /// <summary>Runs the requests in order on one model load (the pipeline's map pass over every chunk).</summary>
    public async Task<IReadOnlyList<AiResponse>> GenerateManyAsync(IReadOnlyList<AiRequest> requests, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(requests);
        if (requests.Count == 0)
        {
            return [];
        }

        foreach (var request in requests)
        {
            request.Validate();
        }

        var started = Stopwatch.GetTimestamp();
        var job = new LocalLlmJob
        {
            ModelPath = _modelPath,
            ModelId = _model.Id,
            ModelName = _model.Name,
            Profile = _model.Llm,
            Device = _options.Device,
            ContextTokens = _options.ContextTokens,
            FreeVramBytes = _freeVram(),
            VramMarginBytes = _options.VramMarginBytes,
            SpillThresholdBytes = _options.SpillThresholdBytes,
            Threads = _options.Threads,
            Prompts = requests.Select(ToPrompt).ToList(),
        };

        var relay = progress is null ? null : new LocalProgressRelay(progress);
        LocalLlmResult result;
        try
        {
            result = await _client.RunAsync(job, relay, cancellationToken);
        }
        catch (LocalLlmException ex)
        {
            throw new AiException(ex.Error, ex);
        }

        var total = Stopwatch.GetElapsedTime(started);
        var responses = new List<AiResponse>(requests.Count);
        for (var i = 0; i < requests.Count; i++)
        {
            var output = result.Outputs.FirstOrDefault(o => o.Index == i)
                ?? throw new AiException(AiErrors.LocalFailed(DisplayName, _model.Name, "an answer is missing from the result"));
            responses.Add(ToResponse(requests[i], output, result, total, i == 0, cancellationToken));
        }

        progress?.Report(new AiProgress(AiProgressStage.Done, OutputTokens: result.Outputs.Sum(o => o.OutputTokens)));
        return responses;
    }

    private AiResponse ToResponse(AiRequest request, LocalLlmOutput output, LocalLlmResult result, TimeSpan total, bool first, CancellationToken cancellationToken)
    {
        if (output.StopReason == LocalLlmStopReasons.Cancelled)
        {
            throw new OperationCanceledException("The local generation was cancelled.", cancellationToken);
        }

        if (output.StopReason == LocalLlmStopReasons.ContextFull)
        {
            throw new AiException(AiErrors.LocalContentTooLong(DisplayName, output.PromptTokens + request.MaxOutputTokens, result.Device.ContextTokens));
        }

        var stop = LocalLlmStopReasons.ToAi(output.StopReason);
        var json = request.ExpectsJson && stop == AiStopReason.Completed
            ? CloudJson.ParseJsonAnswer(output.Text) ?? throw new AiException(AiErrors.LocalFailed(DisplayName, _model.Name, "the answer is not valid JSON"))
            : (System.Text.Json.JsonElement?)null;
        var timings = new AiTimings(
            total,
            PromptEvaluation: TimeSpan.FromMilliseconds(output.PromptMs),
            Generation: TimeSpan.FromMilliseconds(output.GenerateMs),
            ModelLoad: first ? TimeSpan.FromMilliseconds(result.LoadMs + result.WarmUpMs) : null);
        return new AiResponse(
            Id,
            _model.Id,
            output.Text,
            json,
            stop,
            output.StopReason,
            new AiUsage(output.PromptTokens, output.OutputTokens),
            timings,
            AiRequestHash.Compute(request, Id, _model.Id));
    }

    private static LocalLlmPrompt ToPrompt(AiRequest request) => new(
        request.Purpose,
        request.System,
        request.Messages.Select(m => new LocalLlmTurn(m.Role == AiRole.User ? LocalLlmTurn.User : LocalLlmTurn.Assistant, m.Content)).ToList(),
        request.MaxOutputTokens,
        request.Grammar ?? (request.JsonSchema is { } schema ? JsonSchemaGrammar.FromSchema(schema) : null),
        request.Temperature ?? 0);

    /// <summary>Turns worker lines into provider progress.</summary>
    private sealed class LocalProgressRelay(IProgress<AiProgress> target) : IProgress<LocalLlmWorkerReply>
    {
        public void Report(LocalLlmWorkerReply value)
        {
            if (value.LlmProgress is not { } progress)
            {
                return;
            }

            var stage = progress.Phase switch
            {
                LocalLlmProgress.Loading or LocalLlmProgress.WarmingUp => AiProgressStage.Loading,
                LocalLlmProgress.Generating => AiProgressStage.Generating,
                _ => AiProgressStage.Sending,
            };
            target.Report(new AiProgress(stage, progress.Delta, progress.OutputTokens, Index: progress.PromptIndex >= 0 ? progress.PromptIndex : null));
        }
    }
}
