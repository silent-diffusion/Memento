using System.Text.RegularExpressions;
using Memento.AI.Local;
using Memento.Core.Engines;

namespace Memento.AI.Tests.Local;

/// <summary>
/// <see cref="MemoryPlanner"/> on the reference laptop's numbers (ENGINE-NOTES.md §T): every branch (all on the card, the
/// experts in RAM, a split by blocks, RAM only, does not fit), a transcription waiting, a recording being captured, the
/// correction from a measured load, and the sentences.
/// </summary>
public sealed class MemoryPlannerTests
{
    private const long MiB = 1024 * 1024;
    private const long GiB = 1024 * MiB;

    /// <summary>Qwen3.5 35B-A3B as the catalog describes it.</summary>
    private static LocalModelProfile Moe => LocalModelCatalog.Find(LocalModelCatalog.Qwen35MoE)!.Llm;

    /// <summary>A dense profile shaped like Qwen3.5 27B (64 blocks of 230 MiB, a 1000 MiB output layer).</summary>
    private static readonly LocalModelProfile Dense = new(
        "qwen3.5", "qwen35", 16384, 8192, 16384, 64, 0, 65536, 3000,
        LayerBytes: 230 * MiB, OutputBytes: 1000 * MiB, HostBytes: 1700 * MiB, GpuComputeBytes: 600 * MiB);

    private static MemoryPlanInput Pc(long? freeVram = 5222 * MiB, long availableRam = 24 * GiB, MemoryDemands? demands = null) =>
        new(freeVram, availableRam, 40 * GiB, 16, demands ?? MemoryDemands.None);

    [Fact]
    public void AModelThatFitsTheCardRunsThereWithLittleRam()
    {
        var plan = MemoryPlanner.Plan(Dense, Pc(freeVram: 24 * GiB));

        Assert.True(plan.Fits);
        Assert.Equal(LocalPlacements.Card, plan.Placement);
        Assert.Equal(65, plan.GpuLayers);
        Assert.Equal(64, plan.CardBlocks);
        Assert.Equal(16384, plan.ContextTokens);
        Assert.Equal(Dense.HostBytes, plan.RamBytes);
        Assert.Equal(LocalLlmDevices.Gpu, plan.Device);
    }

    [Fact]
    public void AMixtureOfExpertsKeepsTheExpertsOfTheFirstBlocksInRamAndTheRestOnTheCard()
    {
        var plan = MemoryPlanner.Plan(Moe, Pc());

        Assert.True(plan.Fits);
        Assert.Equal(LocalPlacements.Experts, plan.Placement);
        Assert.Equal(41, plan.GpuLayers);
        Assert.Equal(40, plan.CardBlocks);
        // 5.1 GB free, 0.5 GB margin, 3.1 GB for attention, the KV cache and the buffers: three blocks' experts fit.
        Assert.Equal(37, plan.CpuExpertLayers);
        Assert.True(plan.VramBytes <= (5222 - 512) * MiB);
        Assert.InRange(plan.RamBytes / (double)GiB, 17.5, 18.0);
        Assert.InRange(plan.TokensPerSecond, 10, 14);
        Assert.Equal(8, plan.Threads);
    }

    [Fact]
    public void ADenseModelSplitsByBlocksWithTheLastOnesOnTheCard()
    {
        var plan = MemoryPlanner.Plan(Dense, Pc());

        Assert.True(plan.Fits);
        Assert.Equal(LocalPlacements.Split, plan.Placement);
        Assert.Equal(12, plan.CardBlocks);
        Assert.Equal(13, plan.GpuLayers);
        Assert.Equal(0, plan.CpuExpertLayers);
        Assert.True(plan.RamBytes > 52 * 230 * MiB);
        Assert.Equal(LocalLlmDevices.Gpu, plan.Device);
    }

    [Fact]
    public void WithoutACardOrWithTooLittleOfItTheModelRunsInRam()
    {
        var none = MemoryPlanner.Plan(Dense, Pc(freeVram: null));
        var little = MemoryPlanner.Plan(Dense, Pc(freeVram: 3 * GiB));

        Assert.Equal(LocalPlacements.Ram, none.Placement);
        Assert.Equal(0, none.GpuLayers);
        Assert.Equal(0, none.VramBytes);
        Assert.Equal(LocalLlmDevices.Cpu, none.Device);
        Assert.True(none.RamBytes >= Dense.WeightBytes);

        // 3 GB leaves room for 3 of 64 blocks: below the 15 % worth holding the card for.
        Assert.Equal(LocalPlacements.Ram, little.Placement);
    }

    [Fact]
    public void AModelThatDoesNotFitIsRefusedWithWhatItNeedsAndWhatIsFree()
    {
        var plan = MemoryPlanner.Plan(Moe, Pc(availableRam: 8 * GiB));

        Assert.False(plan.Fits);
        Assert.Equal(LocalPlacements.None, plan.Placement);
        Assert.Equal(8192, plan.ContextTokens);
        var sentence = MemoryPlanWording.Refusal("Qwen3.5 35B-A3B", plan);
        Assert.Matches(new Regex(@"^Qwen3\.5 35B-A3B needs \d+ GB of memory; this PC has \d+ GB free\. Close other programs or choose a smaller model\.$"), sentence);
        Assert.True(plan.NeededBytes > plan.FreeBytes);
        Assert.Contains("needs 25 GB", sentence, StringComparison.Ordinal);
        Assert.Contains("has 12 GB free", sentence, StringComparison.Ordinal);
    }

    [Fact]
    public void AWaitingTranscriptionKeepsItsVideoMemoryAndItsRam()
    {
        var waiting = new MemoryDemands(false, false, true, 2560 * MiB, 512 * MiB);

        var plan = MemoryPlanner.Plan(Moe, Pc(availableRam: 30 * GiB, demands: waiting));

        // With RAM to spare, 2.5 GB of the card stays free for Whisper: the rest of the model no longer fits beside it, so
        // it runs in RAM on the processor and the transcription keeps the card.
        Assert.True(plan.Fits);
        Assert.Equal(LocalPlacements.Ram, plan.Placement);
        Assert.Equal(0, plan.VramBytes);

        // Without that much RAM it only fits with the card: it is planned there and waits at the gate for the transcription.
        var waits = MemoryPlanner.Plan(Moe, Pc(demands: waiting));
        Assert.True(waits.Fits);
        Assert.Equal(LocalPlacements.Experts, waits.Placement);

        // Whisper on the processor needs RAM: the same PC without it fits, with it does not.
        var tight = MemoryPlanner.Plan(Moe, Pc(availableRam: (long)(20.6 * GiB)));
        var tightWaiting = MemoryPlanner.Plan(Moe, Pc(availableRam: (long)(20.6 * GiB), demands: waiting with { TranscriptionVramBytes = 0 }));
        Assert.True(tight.Fits);
        Assert.False(tightWaiting.Fits);
    }

    [Fact]
    public void ARecordingBeingCapturedKeepsItsRam()
    {
        var ram = (long)(20.5 * GiB);
        var idle = MemoryPlanner.Plan(Moe, Pc(availableRam: ram));
        var recording = MemoryPlanner.Plan(Moe, Pc(availableRam: ram, demands: new MemoryDemands(true, false, false, 0, 0)));
        var video = MemoryHeadroom.Default.RamReserve(new MemoryDemands(true, true, false, 0, 0));

        Assert.True(idle.Fits);
        Assert.False(recording.Fits);
        Assert.Equal(MemoryHeadroom.Default.OsBytes + MemoryHeadroom.Default.AppBytes + MemoryHeadroom.Default.CaptureBytes + MemoryHeadroom.Default.CaptureVideoBytes, video);
    }

    [Fact]
    public void WindowsAlwaysKeepsTwoGigabytes()
    {
        var reserve = MemoryHeadroom.Default.RamReserve(MemoryDemands.None);
        var plan = MemoryPlanner.Plan(Moe, Pc());

        Assert.True(reserve >= 2 * GiB);
        Assert.True(24 * GiB - plan.RamBytes >= 2 * GiB);
    }

    [Fact]
    public void AMeasuredLoadReplacesTheSpeedAndNeverShrinksTheEstimates()
    {
        var first = MemoryPlanner.Plan(Moe, Pc());
        var same = new LocalLoadMeasurement(LocalModelCatalog.Qwen35MoE, first.GpuLayers, first.CpuExpertLayers, first.ContextTokens, first.VramBytes, first.RamBytes, 9.5, first.VramBytes, first.RamBytes, DateTimeOffset.UnixEpoch);
        var bigger = same with { VramBytes = (long)(first.VramBytes * 1.2), GpuLayers = 41, CpuExpertLayers = 40, TokensPerSecond = 0 };
        var smaller = same with { VramBytes = first.VramBytes / 2, RamBytes = first.RamBytes / 2, TokensPerSecond = 0 };

        Assert.Equal(9.5, MemoryPlanner.Plan(Moe, Pc(), same).TokensPerSecond);
        Assert.True(MemoryPlanner.Plan(Moe, Pc(), bigger).CpuExpertLayers > first.CpuExpertLayers);
        var unchanged = MemoryPlanner.Plan(Moe, Pc(), smaller);
        Assert.Equal(first.CpuExpertLayers, unchanged.CpuExpertLayers);
        Assert.Equal(first.RamBytes, unchanged.RamBytes);
    }

    [Fact]
    public void ASpeedMeasuredAtAnotherPlacementScalesTheEstimate()
    {
        var first = MemoryPlanner.Plan(Moe, Pc());
        var elsewhere = new LocalLoadMeasurement(LocalModelCatalog.Qwen35MoE, 0, 0, 16384, 0, 20 * GiB, 3.0, 0, 20 * GiB, DateTimeOffset.UnixEpoch);

        var corrected = MemoryPlanner.Plan(Moe, Pc(), elsewhere);

        Assert.True(corrected.TokensPerSecond < first.TokensPerSecond);
        Assert.Equal(first.CpuExpertLayers, corrected.CpuExpertLayers);
    }

    [Fact]
    public void TheLinesSayWhereHowFastAndHowMuchRam()
    {
        Assert.Equal("Runs on the graphics card with the experts of 37 of 40 layers in RAM · about 12 tokens/s · 18 GB of RAM while writing", MemoryPlanWording.Line(MemoryPlanner.Plan(Moe, Pc())));
        Assert.StartsWith("Runs 12 of 64 layers on the graphics card, the rest in RAM · about ", MemoryPlanWording.Line(MemoryPlanner.Plan(Dense, Pc())), StringComparison.Ordinal);
        Assert.StartsWith("Runs in RAM on the processor · about ", MemoryPlanWording.Line(MemoryPlanner.Plan(Dense, Pc(freeVram: null))), StringComparison.Ordinal);
        Assert.StartsWith("Runs on the graphics card · about ", MemoryPlanWording.Line(MemoryPlanner.Plan(Dense, Pc(freeVram: 24 * GiB))), StringComparison.Ordinal);
        Assert.Equal("graphics card and RAM", MemoryPlanWording.Where(MemoryPlanner.Plan(Moe, Pc())));
        Assert.Equal("processor", MemoryPlanWording.Where(MemoryPlanner.Plan(Dense, Pc(freeVram: null))));
        Assert.Equal("about 1.4 tokens/s", MemoryPlanWording.Speed(1.43));
        Assert.Equal("about 6 tokens/s", MemoryPlanWording.Speed(6.2));
    }

    [Fact]
    public void TheLoadedLineSaysWhatTheWorkerReallyDid()
    {
        var load = new LocalLoadMeasurement(LocalModelCatalog.Qwen35MoE, 41, 40, 16384, 3 * GiB, 19 * GiB, 12.34, 0, 0, DateTimeOffset.UnixEpoch);

        Assert.Equal("Ran on the graphics card with its experts in RAM · 12.3 tokens/s · 19 GB of RAM", MemoryPlanWording.Loaded(Moe, load));
        Assert.Equal("Ran 28 of 64 layers on the graphics card, the rest in RAM · 6.2 tokens/s · 18 GB of RAM", MemoryPlanWording.Loaded(Dense, load with { GpuLayers = 29, CpuExpertLayers = 0, TokensPerSecond = 6.2, RamBytes = 18 * GiB }));
        Assert.Equal("Ran in RAM on the processor · 2.0 tokens/s · 18 GB of RAM", MemoryPlanWording.Loaded(Dense, load with { GpuLayers = 0, CpuExpertLayers = 0, TokensPerSecond = 2, RamBytes = 18 * GiB }));
    }

    [Fact]
    public void LessOffloadMovesExpertsFirstThenBlocksThenGivesUp()
    {
        var plan = MemoryPlanner.Plan(Moe, Pc());
        var placement = LocalLlmPlacement.From(plan, MemoryHeadroom.Default, 2560 * MiB);

        var experts = placement.LessOffload(Moe, plan.ContextTokens, 600 * MiB)!;
        Assert.Equal(placement.GpuLayers, experts.GpuLayers);
        Assert.Equal(39, experts.CpuExpertLayers);
        Assert.True(experts.PlannedVramBytes < placement.PlannedVramBytes);
        Assert.True(experts.PlannedRamBytes > placement.PlannedRamBytes);

        var allExperts = placement with { CpuExpertLayers = 40 };
        var blocks = allExperts.LessOffload(Moe, plan.ContextTokens, 600 * MiB)!;
        Assert.True(blocks.GpuLayers < allExperts.GpuLayers);
        Assert.Equal(40, blocks.CpuExpertLayers);

        var dense = LocalLlmPlacement.From(MemoryPlanner.Plan(Dense, Pc()), MemoryHeadroom.Default, 2560 * MiB);
        var fewer = dense.LessOffload(Dense, 16384, 400 * MiB)!;
        // 400 MB and a quarter more, at 246 MB a block (its weights and KV cache): three blocks move to RAM.
        Assert.Equal(dense.GpuLayers - 3, fewer.GpuLayers);
        Assert.Null(dense.LessOffload(Dense, 16384, 8 * GiB));
    }

    [Fact]
    public void TheExpertPatternNamesTheRoutedExpertsOfTheFirstBlocksOnly()
    {
        var three = new Regex(LlamaLocalLlmEngineFactory.ExpertPattern(3, 40));
        var all = new Regex(LlamaLocalLlmEngineFactory.ExpertPattern(40, 40));

        Assert.Matches(three, "blk.0.ffn_up_exps.weight");
        Assert.Matches(three, "blk.2.ffn_down_exps.weight");
        Assert.DoesNotMatch(three, "blk.3.ffn_gate_exps.weight");
        Assert.DoesNotMatch(three, "blk.12.ffn_up_exps.weight");
        Assert.DoesNotMatch(three, "blk.1.ffn_up_shexp.weight");
        Assert.DoesNotMatch(three, "blk.1.attn_qkv.weight");
        Assert.Matches(all, "blk.39.ffn_gate_exps.weight");
        Assert.DoesNotMatch(all, "blk.39.ffn_gate_shexp.weight");
    }

    [Fact]
    public void ThisLlamaSharpCanLoadWithoutRepacking() => Assert.True(LlamaWeightsLoader.IsAvailable);
}
