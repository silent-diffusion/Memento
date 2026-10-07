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
    public void ThereIsNoRetryAtTheSmallestContextOnTheProcessorOrWithFixedLayers()
    {
        Assert.Null(LlamaLocalLlmEngineFactory.SmallerContext(Job(context: Qwen.Llm.MinContextTokens)));
        Assert.Null(LlamaLocalLlmEngineFactory.SmallerContext(Job(device: LocalLlmDevices.Cpu)));
        Assert.Null(LlamaLocalLlmEngineFactory.SmallerContext(Job(layers: 20)));
    }
}
