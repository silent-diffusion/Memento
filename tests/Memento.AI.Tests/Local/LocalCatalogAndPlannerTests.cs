using System.Text.Json;
using Memento.AI.Local;
using Memento.Core.Models;

namespace Memento.AI.Tests.Local;

public sealed class LocalCatalogAndPlannerTests
{
    private const long MiB = 1024 * 1024;

    private static readonly JsonSerializerOptions WebJson = new(JsonSerializerDefaults.Web);

    [Fact]
    public void TheCatalogCarriesBothVerifiedModels()
    {
        var qwen = LocalModelCatalog.Find(LocalModelCatalog.Qwen35FourB)!;
        var ministral = LocalModelCatalog.Find(LocalModelCatalog.Ministral3ThreeB)!;

        Assert.Equal(2_740_937_888, qwen.SizeBytes);
        Assert.Equal("00fe7986ff5f6b463e62455821146049db6f9313603938a70800d1fb69ef11a4", qwen.Sha256);
        Assert.Equal("https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/e87f176479d0855a907a41277aca2f8ee7a09523/Qwen3.5-4B-Q4_K_M.gguf", qwen.Url);
        Assert.Equal(LocalChatTemplates.Qwen35, qwen.Llm.TemplateId);
        Assert.Equal(16_384, qwen.Llm.ContextTokens);
        Assert.Equal(3.3, qwen.MinVramBytes!.Value / (1024.0 * 1024 * 1024), 2);

        Assert.Equal(2_147_023_008, ministral.SizeBytes);
        Assert.Equal("9ed150d4367e68df0ac8e1540f6ddc65b42d0ee26378329d1ecbca60f93fc5f8", ministral.Sha256);
        Assert.Equal(LocalChatTemplates.Ministral3, ministral.Llm.TemplateId);
        Assert.Equal(4_096, ministral.Llm.CpuContextTokens);
        Assert.Equal("cpu", ministral.RecommendedFor);

        Assert.All(LocalModelCatalog.Models, m =>
        {
            Assert.Equal("Apache-2.0", m.License);
            Assert.StartsWith("https://huggingface.co/", m.Url, StringComparison.Ordinal);
            Assert.Equal(64, m.Sha256.Length);
            Assert.True(LocalChatTemplates.IsKnown(m.Llm.TemplateId));
        });
    }

    [Fact]
    public void TheLocalModelsAreTheLlmEntriesOfCoresCatalogWithTheirLlmBlock()
    {
        var entries = ModelCatalog.Default.OfKind(ModelKinds.Llm).ToList();

        Assert.Equal([LocalModelCatalog.Qwen35FourB, LocalModelCatalog.Ministral3ThreeB, LocalModelCatalog.Qwen35TwentySevenB, LocalModelCatalog.Qwen35MoE], entries.Select(m => m.Id));
        Assert.All(entries, m => Assert.True(m.ExtensionData!.ContainsKey("llm")));
        Assert.All(entries, m => Assert.Equal(LocalModelCatalog.Engine, m.Engine));
        Assert.Equal(entries.Select(e => e.Id), LocalModelCatalog.Models.Select(m => m.Id));
    }

    [Fact]
    public void TheLargerModelsAreVerifiedApacheModelsThatCanBeSplit()
    {
        var dense = ModelCatalog.Default.Find(LocalModelCatalog.Qwen35TwentySevenB)!;
        var moe = ModelCatalog.Default.Find(LocalModelCatalog.Qwen35MoE)!;

        Assert.Equal(16_740_812_704, dense.SizeBytes);
        Assert.Equal("84b5f7f112156d63836a01a69dc3f11a6ba63b10a23b8ca7a7efaf52d5a2d806", dense.Sha256);
        Assert.Equal("https://huggingface.co/unsloth/Qwen3.5-27B-GGUF/resolve/3221f178a6b842d04f1fb42f1c413534adcc0a6a/Qwen3.5-27B-Q4_K_M.gguf", dense.Url);
        Assert.Equal(22_016_023_168, moe.SizeBytes);
        Assert.Equal("3b46d1066bc91cc2d613e3bc22ce691dd77e6f0d33c9060690d24ce6de494375", moe.Sha256);
        Assert.Equal("https://huggingface.co/unsloth/Qwen3.5-35B-A3B-GGUF/resolve/bc014a17be43adabd7066b7a86075ff935c6a4e2/Qwen3.5-35B-A3B-Q4_K_M.gguf", moe.Url);
        Assert.All([dense, moe], m =>
        {
            Assert.Equal(ModelGroups.Larger, m.Group);
            Assert.Equal("Apache-2.0", m.License);
            Assert.Equal("either", m.RunsOn);
            Assert.Null(m.RecommendedFor);
            Assert.Null(m.MinVramBytes);
            Assert.Matches("^[0-9a-f]{64}$", m.Sha256);
            Assert.InRange(m.SizeBytes, 15L << 30, 25L << 30);
            var local = LocalModelCatalog.ToLocal(m)!;
            Assert.True(local.Llm.CanSplit);
            Assert.Equal(LocalChatTemplates.Qwen35, local.Llm.TemplateId);

            // The weights the profile places are the file within 5 % (the embeddings are in the host part).
            Assert.InRange(local.Llm.WeightBytes + local.Llm.HostBytes, m.SizeBytes * 0.95, m.SizeBytes * 1.10);

            // The catalog's minimum memory is the planner's footprint at the smallest context, without the reserves.
            Assert.Equal(local.Llm.HostBytes + local.Llm.WeightBytes + (local.Llm.KvBytesPerToken * local.Llm.MinContextTokens) + MemoryHeadroom.Default.CpuComputeBytes, m.MinRamBytes);
        });
        Assert.False(LocalModelCatalog.ToLocal(dense)!.Llm.IsMixtureOfExperts);
        Assert.True(LocalModelCatalog.ToLocal(moe)!.Llm.IsMixtureOfExperts);
        Assert.Equal("qwen35", LocalModelCatalog.ToLocal(dense)!.Llm.Architecture);
        Assert.Equal("qwen35moe", LocalModelCatalog.ToLocal(moe)!.Llm.Architecture);
        Assert.Equal(8.0 / 256, LocalModelCatalog.ToLocal(moe)!.Llm.ActiveExpertShare);
        Assert.All(ModelCatalog.Default.OfKind(ModelKinds.Llm).Where(m => m.Group is null), m => Assert.False(LocalModelCatalog.ToLocal(m)!.Llm.CanSplit));
    }

    [Fact]
    public void AnEntryWithoutAReadableLlmBlockIsNotALocalModel()
    {
        var entry = ModelCatalog.Default.Find(LocalModelCatalog.Qwen35FourB)!;
        var broken = entry with { ExtensionData = new() { ["llm"] = JsonSerializer.SerializeToElement(new { templateId = "unknown" }, WebJson) } };

        Assert.NotNull(LocalModelCatalog.ToLocal(entry));
        Assert.Null(LocalModelCatalog.ToLocal(broken));
        Assert.Null(LocalModelCatalog.ToLocal(entry with { ExtensionData = null }));
    }

    [Fact]
    public void TheMeasuredVramFiguresReproduceTheSpike()
    {
        var qwen = LocalModelCatalog.Find(LocalModelCatalog.Qwen35FourB)!.Llm;
        var ministral = LocalModelCatalog.Find(LocalModelCatalog.Ministral3ThreeB)!.Llm;

        // Measured totals: Qwen 3,441 MiB at 8k and 3,728 at 16k; Ministral 2,751 MiB at 4k.
        Assert.InRange(qwen.VramBytes(8192) / MiB, 3400, 3480);
        Assert.InRange(qwen.VramBytes(16384) / MiB, 3680, 3780);
        Assert.InRange(ministral.VramBytes(4096) / MiB, 2740, 2820);
    }

    [Fact]
    public void ThePlannerPicksTheLargestContextThatFitsThenTheProcessor()
    {
        var qwen = LocalModelCatalog.Find(LocalModelCatalog.Qwen35FourB)!.Llm;
        const long margin = 256 * MiB;

        var roomy = LocalVramPlanner.Plan(qwen, LocalLlmDevices.Auto, 5226 * MiB, 0, margin);
        var tight = LocalVramPlanner.Plan(qwen, LocalLlmDevices.Auto, 3800 * MiB, 0, margin);
        var short_ = LocalVramPlanner.Plan(qwen, LocalLlmDevices.Auto, 1500 * MiB, 0, margin);
        var required = LocalVramPlanner.Plan(qwen, LocalLlmDevices.Gpu, 1500 * MiB, 0, margin);
        var cpu = LocalVramPlanner.Plan(qwen, LocalLlmDevices.Cpu, 5226 * MiB, 0, margin);
        var unknown = LocalVramPlanner.Plan(qwen, LocalLlmDevices.Auto, null, 0, margin);

        Assert.Equal((true, true, 33, 16384), (roomy.UseGpu, roomy.Fits, roomy.GpuLayers, roomy.ContextTokens));
        Assert.Equal((true, 8192), (tight.UseGpu, tight.ContextTokens));
        Assert.Equal((false, true, 0, 8192), (short_.UseGpu, short_.Fits, short_.GpuLayers, short_.ContextTokens));
        Assert.False(required.Fits);
        Assert.True(required.NeededVramBytes > 3400 * MiB);
        Assert.Equal((false, 8192), (cpu.UseGpu, cpu.ContextTokens));
        Assert.False(unknown.UseGpu);
    }

    [Fact]
    public void ForcedLayersOverrideTheBudget()
    {
        var plan = LocalVramPlanner.Plan(LocalModelCatalog.Find(LocalModelCatalog.Ministral3ThreeB)!.Llm, LocalLlmDevices.Auto, 10 * MiB, 4096, 0, forcedGpuLayers: 12);

        Assert.Equal((true, 12, 4096), (plan.UseGpu, plan.GpuLayers, plan.ContextTokens));
    }
}
