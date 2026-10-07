using Memento.AI.Local;

namespace Memento.AI.Tests.Local;

/// <summary>A spill while the model loads is tried once more with the profile's smallest context, and only then.</summary>
public sealed class SpillFallbackTests
{
    private static readonly LocalModelEntry Qwen = LocalModelCatalog.Find(LocalModelCatalog.Qwen35FourB)!;

    private static LocalLlmJob Job(string device = LocalLlmDevices.Gpu, int context = 0, int? layers = null) => new()
    {
        ModelPath = "model.gguf",
        ModelId = Qwen.Id,
        ModelName = Qwen.Name,
        Profile = Qwen.Llm,
        Device = device,
        ContextTokens = context,
        GpuLayers = layers,
    };

    [Fact]
    public void ASpillIsTheSharedMemoryErrorNotEveryVideoMemoryError()
    {
        Assert.True(LlamaLocalLlmEngineFactory.IsSpill(new LocalLlmException(AiErrors.VramSpilled("Local model", "Qwen3.5 4B", 512L << 20))));
        Assert.False(LlamaLocalLlmEngineFactory.IsSpill(new LocalLlmException(AiErrors.NotEnoughVram("Local model", "Qwen3.5 4B", 1L << 30, 3L << 30))));
        Assert.False(LlamaLocalLlmEngineFactory.IsSpill(new LocalLlmException(AiErrors.GpuOutOfMemory("Local model", "Qwen3.5 4B", 1L << 30, "context handle is null"))));
    }

    [Fact]
    public void TheRetryUsesTheSmallestGraphicsCardContextWhenMoreWasAsked()
    {
        Assert.Equal(Qwen.Llm.MinContextTokens, LlamaLocalLlmEngineFactory.SmallerContext(Job()));
        Assert.Equal(Qwen.Llm.MinContextTokens, LlamaLocalLlmEngineFactory.SmallerContext(Job(context: 16384)));
    }

    [Fact]
    public void AModelThatMayRunOnTheProcessorMovesThereWhenTheCardStillSpills()
    {
        var ministral = LocalModelCatalog.Find(LocalModelCatalog.Ministral3ThreeB)!;
        var job = Job() with { Profile = ministral.Llm, AllowCpuFallback = true };

        var processor = LlamaLocalLlmEngineFactory.OnProcessor(job);

        Assert.NotNull(processor);
        Assert.Equal(LocalLlmDevices.Cpu, processor.Device);
        Assert.Equal(ministral.Llm.CpuContextTokens, processor.ContextTokens);
        Assert.Null(LlamaLocalLlmEngineFactory.OnProcessor(Job()));
        Assert.Null(LlamaLocalLlmEngineFactory.OnProcessor(job with { Device = LocalLlmDevices.Cpu }));
    }

    [Theory]
    [InlineData(LocalModelCatalog.Qwen35FourB, false)]
    [InlineData(LocalModelCatalog.Ministral3ThreeB, true)]
    public async Task OnlyAModelThatNeedsNoGraphicsCardIsAllowedOntoTheProcessor(string modelId, bool allowed)
    {
        var jobs = new CapturingJobs();
        var provider = new LocalAiProvider(LocalModelCatalog.Find(modelId)!, "model.gguf", jobs, EstimatingTokenCounter.Generic, () => null);

        await Assert.ThrowsAsync<AiException>(() => provider.GenerateAsync(AiRequest.Create("map.test", "s", "u", 8), null, CancellationToken.None));

        Assert.Equal(allowed, jobs.Last!.AllowCpuFallback);
    }

    /// <summary>Keeps the job a provider sends, and fails it.</summary>
    private sealed class CapturingJobs : ILocalLlmJobClient
    {
        public LocalLlmJob? Last { get; private set; }

        public Task<LocalLlmResult> RunAsync(LocalLlmJob job, IProgress<LocalLlmWorkerReply>? progress, CancellationToken cancellationToken)
        {
            Last = job;
            throw new LocalLlmException(AiErrors.LocalFailed("Local model", job.ModelName, "test"));
        }
    }

    [Fact]
    public void ThereIsNoRetryAtTheSmallestContextOnTheProcessorOrWithFixedLayers()
    {
        Assert.Null(LlamaLocalLlmEngineFactory.SmallerContext(Job(context: Qwen.Llm.MinContextTokens)));
        Assert.Null(LlamaLocalLlmEngineFactory.SmallerContext(Job(device: LocalLlmDevices.Cpu)));
        Assert.Null(LlamaLocalLlmEngineFactory.SmallerContext(Job(layers: 20)));
    }
}
