using System.Globalization;
using System.Diagnostics;
using LLama;
using LLama.Abstractions;
using LLama.Common;
using LLama.Exceptions;
using LLama.Native;
using Microsoft.Extensions.Logging;

namespace Memento.AI.Local;

/// <summary>
/// Loads a GGUF model with LLamaSharp within a video memory budget (ENGINE-NOTES.md section H): reads free VRAM from
/// the injected probe right before loading, plans layers and context, loads the weights, creates the context and
/// checks <c>NativeHandle.IsInvalid</c> (an exhausted GPU returns a null handle instead of throwing, and the first use
/// would be an uncatchable access violation), checks the spill watch after each step, and warms up.
/// <c>MainGpu</c>/<c>SplitMode</c> are set only when the Vulkan build is loaded (setting them on the CPU build makes
/// weight loading fail).
/// </summary>
public sealed partial class LlamaLocalLlmEngineFactory(IGpuVramProbe vramProbe, IGpuProcessMemory processMemory, ILogger<LlamaLocalLlmEngineFactory> logger, ISystemMemoryProbe? systemMemory = null) : ILocalLlmEngineFactory
{
    /// <summary>How many times a larger model's load moves more onto RAM after the card spilled, before giving up on the card.</summary>
    public const int MaxOffloadReductions = 2;

    private readonly ILogger<LlamaLocalLlmEngineFactory> _logger = logger;
    private readonly ISystemMemoryProbe _ram = systemMemory ?? new SystemMemoryProbe();

    /// <summary>The production probes: free VRAM from llama.cpp's Vulkan device, process GPU memory from PDH.</summary>
    public LlamaLocalLlmEngineFactory(ILogger<LlamaLocalLlmEngineFactory> logger)
        : this(new GgmlVramProbe(), new PdhGpuProcessMemory(), logger)
    {
    }

    /// <summary>
    /// Loads the model. When the graphics card spills while the model loads or warms up (Windows moves part of it to
    /// shared memory: free memory as reported is not always memory the driver will give this process), the load is
    /// tried once more with the profile's smallest context before giving up (ENGINE-NOTES.md §H trap 2).
    /// </summary>
    public async Task<ILocalLlmEngine> LoadAsync(LocalLlmJob job, Action<LocalLlmProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
        if (job.Placement is not null)
        {
            return await LoadPlacedAsync(job, progress, cancellationToken);
        }

        try
        {
            try
            {
                return await LoadOnceAsync(job, progress, cancellationToken);
            }
            catch (LocalLlmException ex) when (IsSpill(ex) && SmallerContext(job) is { } smaller)
            {
                LogSmallerContext(job.ModelId, smaller, ex.Error.Diagnostic ?? "-");
                return await LoadOnceAsync(job with { ContextTokens = smaller }, progress, cancellationToken);
            }
        }
        catch (LocalLlmException ex) when (IsSpill(ex) && OnProcessor(job) is { } processor)
        {
            // Another app holds the card's memory: a model that may run on the processor does, slower, rather than fail.
            LogOnProcessor(job.ModelId, ex.Error.Diagnostic ?? "-");
            return await LoadOnceAsync(processor, progress, cancellationToken);
        }
    }

    /// <summary>
    /// A larger model placed by the host: the RAM check first, then the placement, made smaller up front when the card
    /// has less free than planned now, and after a spill moved further onto RAM (experts first, then blocks) up to
    /// <see cref="MaxOffloadReductions"/> times; then in RAM with no layers on the card when the catalog allows it.
    /// </summary>
    private async Task<ILocalLlmEngine> LoadPlacedAsync(LocalLlmJob job, Action<LocalLlmProgress>? progress, CancellationToken cancellationToken)
    {
        var placement = job.Placement!;
        CheckRam(job, placement);
        string? reduced = null;
        if (placement.GpuLayers > 0 && job.Device != LocalLlmDevices.Cpu && FreeNow(job) is { } free)
        {
            var lacking = placement.PlannedVramBytes + placement.VramMarginBytes - free;
            if (lacking > 0 && Reduce(job, placement, lacking) is { } smaller)
            {
                reduced = string.Create(CultureInfo.InvariantCulture, $"the card had {AiErrors.Gigabytes(lacking)} less free than planned");
                LogLessOffload(job.ModelId, placement.GpuLayers, smaller.GpuLayers, placement.CpuExpertLayers, smaller.CpuExpertLayers, reduced);
                placement = smaller;
            }
        }

        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await LoadOnceAsync(job with { Placement = placement }, progress, cancellationToken, reduced);
            }
            catch (LocalLlmException ex) when (IsSpill(ex) && placement.GpuLayers > 0)
            {
                var growth = SharedGrowth(ex);
                var next = attempt < MaxOffloadReductions ? placement.LessOffload(job.Profile, job.ContextTokens, growth) : null;
                if (next is null)
                {
                    if (!job.AllowCpuFallback)
                    {
                        throw;
                    }

                    LogOnProcessor(job.ModelId, ex.Error.Diagnostic ?? "-");
                    next = InRam(placement);
                }

                reduced = string.Create(CultureInfo.InvariantCulture, $"the card spilled {AiErrors.Gigabytes(growth)} to shared memory");
                LogLessOffload(job.ModelId, placement.GpuLayers, next.GpuLayers, placement.CpuExpertLayers, next.CpuExpertLayers, reduced);
                placement = next;
            }
        }
    }

    /// <summary>The placement with less on the card, or in RAM when nothing worth having would be left there and the catalog allows it.</summary>
    private static LocalLlmPlacement? Reduce(LocalLlmJob job, LocalLlmPlacement placement, long lacking) =>
        placement.LessOffload(job.Profile, job.ContextTokens, lacking) ?? (job.AllowCpuFallback ? InRam(placement) : null);

    internal static LocalLlmPlacement InRam(LocalLlmPlacement placement) =>
        placement with { GpuLayers = 0, CpuExpertLayers = 0, PlannedRamBytes = placement.PlannedRamBytes + placement.PlannedVramBytes, PlannedVramBytes = 0 };

    /// <summary>The smaller of the card's free memory as llama.cpp's device reports it now and the host's reading, when the Vulkan build is loaded.</summary>
    private long? FreeNow(LocalLlmJob job)
    {
        if (LlamaNative.EnsureLoaded(true) != "vulkan")
        {
            return null;
        }

        var reading = vramProbe.Read()?.FreeBytes;
        return reading is { } budget && job.FreeVramBytes is { } host ? Math.Min(budget, host) : reading ?? job.FreeVramBytes;
    }

    /// <summary>Refuses before anything loads when RAM is short now (the job may have waited for the card since it was planned).</summary>
    private void CheckRam(LocalLlmJob job, LocalLlmPlacement placement)
    {
        if (_ram.AvailableBytes() is not { } available)
        {
            return;
        }

        var needed = placement.PlannedRamBytes + placement.RamReserveBytes;
        if (available < needed)
        {
            throw new LocalLlmException(AiErrors.NotEnoughMemory(LocalAiProvider.ProviderName, job.ModelName, needed + placement.PlannedVramBytes, available + placement.PlannedVramBytes));
        }
    }

    /// <summary>How much moved to shared memory, from a spill's diagnostic.</summary>
    internal static long SharedGrowth(LocalLlmException exception) =>
        exception.Error.Diagnostic is { } d && d.StartsWith("sharedGrowth=", StringComparison.Ordinal)
            && long.TryParse(d.AsSpan("sharedGrowth=".Length), NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes)
            ? bytes
            : 0;

    /// <summary>The job on the processor, for a model whose catalog entry allows it.</summary>
    internal static LocalLlmJob? OnProcessor(LocalLlmJob job) =>
        job.AllowCpuFallback && job.Device != LocalLlmDevices.Cpu && job.GpuLayers is null
            ? job with { Device = LocalLlmDevices.Cpu, ContextTokens = job.Profile.CpuContextTokens }
            : null;

    internal static bool IsSpill(LocalLlmException exception) =>
        exception.Code == AiErrorCodes.NotEnoughVram && exception.Error.Diagnostic?.StartsWith("sharedGrowth=", StringComparison.Ordinal) == true;

    /// <summary>The profile's smallest context on the graphics card, when the job asked for more.</summary>
    internal static int? SmallerContext(LocalLlmJob job)
    {
        var asked = job.ContextTokens > 0 ? job.ContextTokens : job.Profile.ContextTokens;
        return job.Device != LocalLlmDevices.Cpu && job.GpuLayers is null && asked > job.Profile.MinContextTokens ? job.Profile.MinContextTokens : null;
    }

    private async Task<ILocalLlmEngine> LoadOnceAsync(LocalLlmJob job, Action<LocalLlmProgress>? progress, CancellationToken cancellationToken, string? reduced = null)
    {
        const string provider = LocalAiProvider.ProviderName;
        if (!File.Exists(job.ModelPath))
        {
            throw new LocalLlmException(AiErrors.ModelNotInstalled(provider, job.ModelName));
        }

        // The baseline for the spill watch is taken before anything touches the GPU.
        var abort = new AbortState();
        // Only a GPU load can spill; on the processor the Vulkan build still pins some host memory, which is not a spill.
        var onGpu = false;
        var spill = new GpuSpillWatch(processMemory, job.SpillThresholdBytes, () =>
        {
            if (Volatile.Read(ref onGpu))
            {
                abort.Set();
            }
        });
        LLamaWeights? weights = null;
        LLamaContext? context = null;
        var ownedByEngine = false;
        try
        {
            var backend = await Task.Run(() => LlamaNative.EnsureLoaded(job.Device != LocalLlmDevices.Cpu), cancellationToken);
            if (job.Device == LocalLlmDevices.Gpu && backend != "vulkan")
            {
                throw new LocalLlmException(AiErrors.GpuUnavailable(provider, job.ModelName));
            }

            var reading = backend == "vulkan" ? vramProbe.Read() : null;
            // The device's budget is this process's; the host's figure also counts what other apps hold. Take the smaller.
            var free = backend != "vulkan" ? null
                : reading?.FreeBytes is { } budget && job.FreeVramBytes is { } host ? Math.Min(budget, host)
                : reading?.FreeBytes ?? job.FreeVramBytes;
            var placement = job.Placement;
            var plan = placement is not null
                ? new LocalLlmPlan(backend == "vulkan" && placement.GpuLayers > 0, true, backend == "vulkan" ? placement.GpuLayers : 0, job.ContextTokens > 0 ? job.ContextTokens : job.Profile.ContextTokens, placement.PlannedVramBytes + placement.VramMarginBytes, "placed by the host")
                : LocalVramPlanner.Plan(job.Profile, backend == "vulkan" ? job.Device : LocalLlmDevices.Cpu, free, job.ContextTokens, job.VramMarginBytes, job.GpuLayers);
            LogPlan(job.ModelId, backend, plan.UseGpu, plan.GpuLayers, plan.ContextTokens, plan.Reason);
            Volatile.Write(ref onGpu, plan.UseGpu);
            if (!plan.Fits)
            {
                throw new LocalLlmException(AiErrors.NotEnoughVram(provider, job.ModelName, free ?? 0, plan.NeededVramBytes));
            }

            var threads = job.Threads > 0 ? job.Threads : Math.Max(1, Environment.ProcessorCount / 2);
            var parameters = new ModelParams(job.ModelPath)
            {
                ContextSize = (uint)plan.ContextTokens,
                GpuLayerCount = plan.UseGpu ? plan.GpuLayers : 0,
                BatchSize = 2048,
                UBatchSize = 512,
                TypeK = GGMLType.GGML_TYPE_F16,
                TypeV = GGMLType.GGML_TYPE_F16,
                Threads = threads,
                BatchThreads = threads,
                UseMemorymap = true,
                UseMemoryLock = false,
                FlashAttention = job.FlashAttention,
            };
            var cpuExperts = plan.UseGpu && placement is { CpuExpertLayers: > 0 } placed ? Math.Min(placed.CpuExpertLayers, job.Profile.Layers) : 0;
            if (cpuExperts > 0)
            {
                // --n-cpu-moe: the routed experts of the first blocks stay in RAM; attention and the shared expert go on the card.
                parameters.TensorBufferOverrides.Add(new TensorBufferOverride(ExpertPattern(cpuExperts, job.Profile.Layers), "CPU"));
            }

            if (backend == "vulkan")
            {
                parameters.SplitMode = GPUSplitMode.None;
                parameters.MainGpu = 0;
                if (!plan.UseGpu)
                {
                    // On the processor with the Vulkan build loaded: keep the KV cache and the matrix work off the GPU.
                    parameters.NoKqvOffload = true;
                    parameters.OpOffload = false;
                }
            }

            progress?.Invoke(new LocalLlmProgress(LocalLlmProgress.Loading, -1, job.Prompts.Count));
            var started = Stopwatch.GetTimestamp();
            try
            {
                // A larger model in RAM on the CPU build: no repacking. The CPU build's repacked copy of a 22 GB model is
                // another 11.5 GB of private memory for no gain in speed (ENGINE-NOTES.md §T).
                weights = placement is not null && backend == "cpu"
                    ? await Task.Run(() => LlamaWeightsLoader.LoadWithoutRepack(parameters), cancellationToken)
                    : await LLamaWeights.LoadFromFileAsync(parameters, cancellationToken);
            }
            catch (LoadWeightsFailedException ex)
            {
                throw plan.UseGpu
                    ? new LocalLlmException(AiErrors.GpuOutOfMemory(provider, job.ModelName, free, "weights failed to load on the GPU"), ex)
                    : new LocalLlmException(AiErrors.LocalFailed(provider, job.ModelName, "the model file could not be loaded"), ex);
            }

            ThrowIfSpilled(spill, job, plan.UseGpu);
            try
            {
                context = weights.CreateContext(parameters);
            }
            catch (RuntimeError ex)
            {
                throw new LocalLlmException(AiErrors.GpuOutOfMemory(provider, job.ModelName, free, "context creation failed"), ex);
            }

            if (context.NativeHandle.IsInvalid)
            {
                throw new LocalLlmException(AiErrors.GpuOutOfMemory(provider, job.ModelName, free, "context handle is null"));
            }

            ThrowIfSpilled(spill, job, plan.UseGpu);
            var loadMs = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            var device = new LocalLlmDeviceInfo(
                plan.UseGpu ? "vulkan" : "cpu",
                plan.UseGpu ? reading?.Name : null,
                plan.UseGpu ? plan.GpuLayers : 0,
                (int)context.ContextSize,
                threads,
                free,
                plan.UseGpu ? plan.NeededVramBytes - (placement?.VramMarginBytes ?? job.VramMarginBytes) : null,
                cpuExperts,
                null,
                reduced);
            var engine = new LlamaLocalLlmEngine(job, weights, context, device, spill, abort, loadMs);
            weights = null;
            context = null;
            ownedByEngine = true;
            try
            {
                if (job.WarmUp)
                {
                    progress?.Invoke(new LocalLlmProgress(LocalLlmProgress.WarmingUp, -1, job.Prompts.Count));
                    await engine.WarmUpAsync(cancellationToken);
                }

                spill.Check();
                engine.DedicatedVramBytes = spill.LastDedicatedBytes;
                engine.Device = engine.Device with { RamBytes = _ram.WorkingSetBytes() };
                ThrowIfSpilled(spill, job, plan.UseGpu);
                if (plan.UseGpu)
                {
                    spill.Start(TimeSpan.FromMilliseconds(500));
                }
                return engine;
            }
            catch
            {
                engine.Dispose();
                throw;
            }
        }
        catch when (!ownedByEngine)
        {
            // Not handed to an engine yet: release what was loaded, context first.
            context?.Dispose();
            weights?.Dispose();
            spill.Dispose();
            abort.Dispose();
            throw;
        }
    }

    /// <summary>The tensor names of the routed experts of blocks 0 to <paramref name="blocks"/> - 1 (of any block when that is all of them).</summary>
    internal static string ExpertPattern(int blocks, int layers) =>
        blocks >= layers
            ? @"\.ffn_(up|down|gate)_exps"
            : string.Create(CultureInfo.InvariantCulture, $@"^blk\.({string.Join('|', Enumerable.Range(0, blocks))})\.ffn_(up|down|gate)_exps");

    private static void ThrowIfSpilled(GpuSpillWatch spill, LocalLlmJob job, bool onGpu)
    {
        if (spill.Check() && onGpu)
        {
            throw new LocalLlmException(AiErrors.VramSpilled(LocalAiProvider.ProviderName, job.ModelName, spill.MaxSharedGrowthBytes));
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Local model {ModelId} spilled out of the graphics card's memory while loading ({Diagnostic}); loading again with a {Context}-token context")]
    private partial void LogSmallerContext(string modelId, int context, string diagnostic);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Local model {ModelId} spilled out of the graphics card's memory at its smallest context ({Diagnostic}); loading it on the processor")]
    private partial void LogOnProcessor(string modelId, string diagnostic);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Local model {ModelId}: {Layers} -> {Fewer} GPU layers, experts of {Experts} -> {More} blocks in RAM ({Why})")]
    private partial void LogLessOffload(string modelId, int layers, int fewer, int experts, int more, string why);

    [LoggerMessage(Level = LogLevel.Information, Message = "Local model {ModelId} on {Backend}: GPU {UseGpu}, {Layers} layers, context {Context} ({Reason})")]
    private partial void LogPlan(string modelId, string backend, bool useGpu, int layers, int context, string reason);
}
