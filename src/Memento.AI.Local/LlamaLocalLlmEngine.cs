using System.Diagnostics;
using System.Text;
using LLama;
using LLama.Exceptions;
using LLama.Native;
using LLama.Sampling;

namespace Memento.AI.Local;

/// <summary>
/// One loaded model and context (ENGINE-NOTES.md section H): prompts formatted from the verified template, control
/// tokens parsed only in template pieces, prompt plus output limit checked against <c>n_ctx</c>,
/// <c>DefaultSamplingPipeline { Temperature = 0, TopK = 1, Grammar, GrammarOptimization = Basic }</c>, streaming
/// decode, cancellation per token and through the abort callback during prompt evaluation, the spill watch, calls
/// serialised, and unload context then weights.
/// </summary>
internal sealed class LlamaLocalLlmEngine : ILocalLlmEngine
{
    private readonly LocalLlmJob _job;
    private readonly LLamaWeights _weights;
    private readonly LLamaContext _context;
    private readonly GpuSpillWatch? _spill;
    private readonly AbortState _abort;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _disposed;

    public LlamaLocalLlmEngine(LocalLlmJob job, LLamaWeights weights, LLamaContext context, LocalLlmDeviceInfo device, GpuSpillWatch? spill, AbortState abort, double loadMs)
    {
        _job = job;
        _weights = weights;
        _context = context;
        _spill = spill;
        _abort = abort;
        Device = device;
        LoadMs = loadMs;
        NativeApi.llama_set_abort_callback(context.NativeHandle, AbortState.Callback, abort.UserData);
    }

    public LocalLlmDeviceInfo Device { get; }

    public double LoadMs { get; }

    public double WarmUpMs { get; private set; }

    public long? DedicatedVramBytes { get; internal set; }

    public long? SharedVramGrowthBytes => _spill?.CanWatch == true ? Math.Max(0, _spill.MaxSharedGrowthBytes) : null;

    public int CountTokens(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length == 0 ? 0 : _weights.Tokenize(text, false, false, Encoding.UTF8).Length;
    }

    /// <summary>
    /// A short generation so the first real request does not pay for compiling the GPU shaders (about 8x slower):
    /// on the graphics card a prompt longer than one micro-batch (512 tokens) so the batched matrix kernels are
    /// built too, then a few single-token steps; on the processor a two-token run is enough.
    /// </summary>
    public async Task WarmUpAsync(CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var text = Device.GpuLayers > 0 ? string.Join(' ', Enumerable.Repeat("The quick brown fox jumps over the lazy dog.", 64)) : "Hello";
        var prompt = new LocalLlmPrompt("warmup", string.Empty, [new LocalLlmTurn(LocalLlmTurn.User, text)], 4);
        await GenerateAsync(-1, prompt, null, cancellationToken);
        WarmUpMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
    }

    public async Task<LocalLlmOutput> GenerateAsync(int index, LocalLlmPrompt prompt, Action<string>? onDelta, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Generate(index, prompt, onDelta, cancellationToken), CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }
    }

    private LocalLlmOutput Generate(int index, LocalLlmPrompt prompt, Action<string>? onDelta, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ThrowIfSpilled();
        _abort.Reset();
        using var registration = cancellationToken.Register(_abort.Set);
        var handle = _context.NativeHandle;
        handle.MemoryClear(true);

        var tokens = Tokenize(prompt);
        var contextSize = (int)_context.ContextSize;
        if (tokens.Length + prompt.MaxTokens > contextSize)
        {
            return new LocalLlmOutput(index, string.Empty, LocalLlmStopReasons.ContextFull, tokens.Length, 0, 0, 0);
        }

        using var sampler = new DefaultSamplingPipeline
        {
            Temperature = (float)prompt.Temperature,
            TopK = prompt.Temperature <= 0 ? 1 : 40,
            Seed = 42,
            Grammar = prompt.Grammar is null ? null : new Grammar(prompt.Grammar, "root"),
            GrammarOptimization = DefaultSamplingPipeline.GrammarOptimizationMode.Basic,
        };

        var batch = new LLamaBatch();
        var started = Stopwatch.GetTimestamp();
        // One micro-batch per decode call: with every layer on the GPU the abort callback is only consulted between
        // graph splits, so smaller calls are what keep a cancel during prompt reading under a second.
        var batchSize = (int)Math.Min(_context.BatchSize, 512u);
        for (var i = 0; i < tokens.Length; i += batchSize)
        {
            batch.Clear();
            var count = Math.Min(batchSize, tokens.Length - i);
            for (var j = 0; j < count; j++)
            {
                batch.Add(tokens[i + j], i + j, LLamaSeqId.Zero, i + j == tokens.Length - 1);
            }

            var result = _context.Decode(batch);
            if (result == DecodeResult.ComputeAborted)
            {
                ThrowIfSpilled();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (result != DecodeResult.Ok)
            {
                throw Failure(result);
            }

            ThrowIfSpilled();
            cancellationToken.ThrowIfCancellationRequested();
        }

        handle.Synchronize();
        var promptMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        ThrowIfSpilled();

        started = Stopwatch.GetTimestamp();
        var decoder = new StreamingTokenDecoder(_context);
        var text = new StringBuilder();
        var vocab = _weights.Vocab;
        var position = tokens.Length;
        var logits = batch.TokenCount - 1;
        var generated = 0;
        var stop = LocalLlmStopReasons.MaxTokens;
        while (generated < prompt.MaxTokens)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = sampler.Sample(handle, logits);
            generated++;
            if (token.IsEndOfGeneration(vocab))
            {
                stop = LocalLlmStopReasons.EndOfGeneration;
                break;
            }

            decoder.Add(token);
            var piece = decoder.Read();
            if (piece.Length > 0)
            {
                text.Append(piece);
                onDelta?.Invoke(piece);
            }

            batch.Clear();
            batch.Add(token, position++, LLamaSeqId.Zero, true);
            logits = 0;
            var result = _context.Decode(batch);
            if (result == DecodeResult.ComputeAborted)
            {
                ThrowIfSpilled();
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (result != DecodeResult.Ok)
            {
                throw Failure(result);
            }
        }

        ThrowIfSpilled();
        return new LocalLlmOutput(index, text.ToString(), stop, tokens.Length, generated, promptMs, Stopwatch.GetElapsedTime(started).TotalMilliseconds);
    }

    /// <summary>Template pieces with special-token parsing; content as plain text; BOS as the template writes it.</summary>
    private LLamaToken[] Tokenize(LocalLlmPrompt prompt)
    {
        var turns = prompt.Messages.Select(m => new AiMessage(m.Role == LocalLlmTurn.Assistant ? AiRole.Assistant : AiRole.User, m.Content)).ToList();
        var parts = LocalChatTemplates.RenderParts(_job.Profile.TemplateId, prompt.System, turns);
        var bos = _weights.Vocab.BOS is { } b ? _weights.Vocab.LLamaTokenToString(b, true) : null;
        var hasBosText = !string.IsNullOrEmpty(bos) && parts.Count > 0 && !parts[0].IsContent && parts[0].Text.StartsWith(bos, StringComparison.Ordinal);
        var tokens = new List<LLamaToken>();
        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];
            tokens.AddRange(_context.Tokenize(part.Text, addBos: i == 0 && !hasBosText, special: !part.IsContent));
        }

        return [.. tokens];
    }

    private void ThrowIfSpilled()
    {
        if (Device.GpuLayers > 0 && _spill is { Spilled: true } spill)
        {
            throw new LocalLlmException(AiErrors.VramSpilled(LocalAiProvider.ProviderName, _job.ModelName, spill.MaxSharedGrowthBytes));
        }
    }

    private LocalLlmException Failure(DecodeResult result) => result switch
    {
        DecodeResult.NoKvSlot or DecodeResult.AllocationFailed when Device.GpuLayers > 0 =>
            new LocalLlmException(AiErrors.GpuOutOfMemory(LocalAiProvider.ProviderName, _job.ModelName, Device.FreeVramBytes, "decode " + result)),
        _ => new LocalLlmException(AiErrors.LocalFailed(LocalAiProvider.ProviderName, _job.ModelName, "decode " + result), new LLamaDecodeError(result)),
    };

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gate.Wait();
        try
        {
            _spill?.Dispose();

            // Context first, then weights: this is what releases the video memory (3.1 GB → 8 MB in the spike).
            _context.Dispose();
            _weights.Dispose();
            _abort.Dispose();
        }
        finally
        {
            _gate.Release();
            _gate.Dispose();
        }
    }
}
