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
        Assert.Equal("https://huggingface.co/unsloth/Qwen3.5-4B-GGUF/resolve/main/Qwen3.5-4B-Q4_K_M.gguf", qwen.Url);
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

        Assert.Equal([LocalModelCatalog.Qwen35FourB, LocalModelCatalog.Ministral3ThreeB], entries.Select(m => m.Id));
        Assert.All(entries, m => Assert.True(m.ExtensionData!.ContainsKey("llm")));
        Assert.All(entries, m => Assert.Equal(LocalModelCatalog.Engine, m.Engine));
        Assert.Equal(entries.Select(e => e.Id), LocalModelCatalog.Models.Select(m => m.Id));
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
