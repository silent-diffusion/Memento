using System.Diagnostics;
using LLama;
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
public sealed partial class LlamaLocalLlmEngineFactory(IGpuVramProbe vramProbe, IGpuProcessMemory processMemory, ILogger<LlamaLocalLlmEngineFactory> logger) : ILocalLlmEngineFactory
{
    private readonly ILogger<LlamaLocalLlmEngineFactory> _logger = logger;

    /// <summary>The production probes: free VRAM from llama.cpp's Vulkan device, process GPU memory from PDH.</summary>
    public LlamaLocalLlmEngineFactory(ILogger<LlamaLocalLlmEngineFactory> logger)
        : this(new GgmlVramProbe(), new PdhGpuProcessMemory(), logger)
    {
    }

    public async Task<ILocalLlmEngine> LoadAsync(LocalLlmJob job, Action<LocalLlmProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(job);
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
            var free = reading?.FreeBytes ?? (backend == "vulkan" ? job.FreeVramBytes : null);
            var plan = LocalVramPlanner.Plan(job.Profile, backend == "vulkan" ? job.Device : LocalLlmDevices.Cpu, free, job.ContextTokens, job.VramMarginBytes, job.GpuLayers);
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
            };
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
                weights = await LLamaWeights.LoadFromFileAsync(parameters, cancellationToken);
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
                plan.UseGpu ? plan.NeededVramBytes - job.VramMarginBytes : null);
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

    private static void ThrowIfSpilled(GpuSpillWatch spill, LocalLlmJob job, bool onGpu)
    {
        if (spill.Check() && onGpu)
        {
            throw new LocalLlmException(AiErrors.VramSpilled(LocalAiProvider.ProviderName, job.ModelName, spill.MaxSharedGrowthBytes));
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Local model {ModelId} on {Backend}: GPU {UseGpu}, {Layers} layers, context {Context} ({Reason})")]
    private partial void LogPlan(string modelId, string backend, bool useGpu, int layers, int context, string reason);
}
