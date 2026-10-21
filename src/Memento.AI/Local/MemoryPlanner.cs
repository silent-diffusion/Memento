using System.Globalization;
using Memento.Core.Engines;

namespace Memento.AI.Local;

/// <summary>
/// Decides, before loading, where a local model's weights go (ARCHITECTURE.md §8 "Larger models", ENGINE-NOTES.md §T),
/// from the free video memory (what other programs hold already taken off) and the available RAM measured now:
/// <list type="number">
/// <item>Every layer on the card when the model, its KV cache and compute buffer fit with the margin.</item>
/// <item>A mixture of experts: everything but the routed experts on the card, and the experts of as many of the last
/// blocks as still fit; the experts of the first blocks stay in RAM (llama.cpp's tensor buffer overrides).</item>
/// <item>A split: as many of the last blocks as fit on the card (with their KV cache), the first ones in RAM, when at
/// least <see cref="MemoryHeadroom.MinCardShare"/> of them fit.</item>
/// <item>In RAM on the processor.</item>
/// </list>
/// The first that also fits in RAM with every reserve kept (Windows, the app, a recording being captured, a transcription
/// waiting) is the plan, at the largest context down to the profile's smallest; otherwise the model does not fit. While a
/// transcription is waiting or running the card keeps the video memory it needs, unless the model only fits with it (it
/// then waits for the card, as every job on the card does). A measurement of an earlier load of the same model corrects
/// the estimates (never below them) and replaces the speed estimate.
/// </summary>
public static class MemoryPlanner
{
    /// <summary>Plans <paramref name="profile"/> (a model that can be split) for the PC as <paramref name="input"/> describes it.</summary>
    public static LocalMemoryPlan Plan(LocalModelProfile profile, MemoryPlanInput input, LocalLoadMeasurement? last = null, MemoryHeadroom? headroom = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(input);
        var room = headroom ?? MemoryHeadroom.Default;
        var threads = Math.Clamp(input.LogicalProcessors / 2, 1, 16);
        var ramBudget = input.AvailableRamBytes - room.RamReserve(input.Demands);
        var cardFree = input.FreeVramBytes is { } free && free > 0 ? free : 0;
        var held = input.Demands.TranscriptionPending ? input.Demands.TranscriptionVramBytes : 0;
        var vramScale = Scale(last?.VramBytes, last?.PlannedVramBytes);
        var ramScale = Scale(last?.RamBytes, last?.PlannedRamBytes);
        var whole = Math.Max(0, cardFree - room.VramMarginBytes);
        var minimum = Math.Min(profile.MinContextTokens, profile.ContextTokens);

        LocalMemoryPlan? Fit(long cardBudget)
        {
            for (var context = profile.ContextTokens; context >= minimum; context /= 2)
            {
                foreach (var option in Options(profile, context, cardBudget, room, vramScale, ramScale))
                {
                    if (option.Ram <= ramBudget)
                    {
                        return Build(profile, option, context, threads, input, room, last, ramBudget, cardBudget, true);
                    }
                }

                if (context / 2 < minimum)
                {
                    break;
                }
            }

            return null;
        }

        // A waiting transcription keeps the card's memory it needs; when the model only fits with that memory too, it is
        // planned with the whole card and waits at the gate until the transcription has let go of it (one job on the card).
        var best = (held > 0 ? Fit(Math.Max(0, whole - held)) : null) ?? Fit(whole);
        if (best is not null)
        {
            return best;
        }

        // Does not fit: the smallest placement's whole footprint with the reserves, against what is free.
        var smallest = Options(profile, minimum, whole, room, vramScale, ramScale).Last();
        return Build(profile, smallest with { Placement = LocalPlacements.None }, minimum, threads, input, room, last, ramBudget, whole, false);
    }

    /// <summary>The model's memory with the reserves (RAM and card together), as the §17 sentence states it.</summary>
    public static long NeededBytes(LocalModelProfile profile, int contextTokens, MemoryDemands demands, MemoryHeadroom? headroom = null)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(demands);
        var room = headroom ?? MemoryHeadroom.Default;
        return profile.HostBytes + profile.WeightBytes + (profile.KvBytesPerToken * contextTokens) + room.CpuComputeBytes + room.RamReserve(demands);
    }

    private static LocalMemoryPlan Build(LocalModelProfile profile, Option option, int context, int threads, MemoryPlanInput input, MemoryHeadroom room, LocalLoadMeasurement? last, long ramBudget, long cardBudget, bool fits)
    {
        var estimate = Speed(profile, option, room);
        var speed = estimate;
        if (last is { TokensPerSecond: > 0 } measured)
        {
            if (measured.GpuLayers == option.GpuLayers && measured.CpuExpertLayers == option.CpuExpertLayers)
            {
                speed = measured.TokensPerSecond;
            }
            else
            {
                var then = Speed(profile, Describe(profile, measured.GpuLayers, measured.CpuExpertLayers), room);
                speed = then > 0 ? estimate * Math.Clamp(measured.TokensPerSecond / then, 0.5, 2.0) : estimate;
            }
        }

        var needed = NeededBytes(profile, context, input.Demands, room);
        var freeForIt = Math.Max(0, input.AvailableRamBytes) + cardBudget;
        var reason = string.Create(
            CultureInfo.InvariantCulture,
            $"{option.Placement}: {option.GpuLayers} GPU layers, experts of {option.CpuExpertLayers} blocks in RAM, {Mb(option.Vram)} on the card of {Mb(cardBudget)} usable, {Mb(option.Ram)} RAM of {Mb(ramBudget)} usable, {context} context");
        return new LocalMemoryPlan(
            fits,
            fits ? option.Placement : LocalPlacements.None,
            option.GpuLayers,
            profile.Layers,
            option.CardBlocks,
            option.CpuExpertLayers,
            context,
            threads,
            option.Vram,
            option.Ram,
            Math.Round(speed, 1),
            needed,
            freeForIt,
            reason);
    }

    /// <summary>The placements to try, most of the card first, each with its video memory and RAM.</summary>
    private static IEnumerable<Option> Options(LocalModelProfile p, int context, long cardBudget, MemoryHeadroom room, double vramScale, double ramScale)
    {
        var kv = p.KvBytesPerToken * context;
        var host = (long)(p.HostBytes * ramScale);
        var compute = p.GpuComputeBytes;
        var layers = p.Layers;
        var expertPerLayer = p.ExpertBytesPerLayer;

        // 1. Everything on the card.
        var all = (long)((p.WeightBytes + kv + compute) * vramScale);
        if (all <= cardBudget)
        {
            yield return new Option(LocalPlacements.Card, layers + 1, layers, 0, all, host);
        }

        // 2. A mixture of experts: the rest on the card, experts of the last blocks as they fit, the first blocks' in RAM.
        if (p.IsMixtureOfExperts && expertPerLayer > 0)
        {
            var baseVram = (long)((p.WeightBytes - p.ExpertBytes + kv + compute) * vramScale);
            if (baseVram <= cardBudget)
            {
                var scaledExpert = (long)(expertPerLayer * vramScale);
                var onCard = (int)Math.Min(layers, (cardBudget - baseVram) / Math.Max(1, scaledExpert));
                var inRam = layers - onCard;
                if (inRam > 0)
                {
                    yield return new Option(LocalPlacements.Experts, layers + 1, layers, inRam, baseVram + (onCard * scaledExpert), host + (long)(inRam * expertPerLayer * ramScale));
                }
            }
        }

        // 3. A split by blocks: the last blocks with their KV cache and the output layer on the card.
        var perBlockVram = (long)((p.LayerBytes + expertPerLayer + (kv / Math.Max(1, layers))) * vramScale);
        var fixedVram = (long)((p.OutputBytes + compute) * vramScale);
        var blocks = cardBudget > fixedVram ? (int)Math.Min(layers - 1, (cardBudget - fixedVram) / Math.Max(1, perBlockVram)) : 0;
        if (blocks >= Math.Max(1, (int)Math.Ceiling(layers * room.MinCardShare)))
        {
            var inRam = layers - blocks;
            var ram = host + (long)((inRam * (p.LayerBytes + expertPerLayer + (kv / Math.Max(1, layers)))) * ramScale) + room.CpuComputeBytes;
            yield return new Option(LocalPlacements.Split, blocks + 1, blocks, 0, fixedVram + (blocks * perBlockVram), ram);
        }

        // 4. In RAM on the processor.
        yield return new Option(LocalPlacements.Ram, 0, 0, 0, 0, host + (long)((p.WeightBytes + kv) * ramScale) + room.CpuComputeBytes);
    }

    /// <summary>The option a measured load stands for, to compare its speed with the estimate.</summary>
    private static Option Describe(LocalModelProfile p, int gpuLayers, int cpuExpertLayers)
    {
        if (gpuLayers <= 0)
        {
            return new Option(LocalPlacements.Ram, 0, 0, 0, 0, 0);
        }

        var blocks = Math.Min(p.Layers, gpuLayers - 1);
        var placement = cpuExpertLayers > 0 ? LocalPlacements.Experts : blocks >= p.Layers ? LocalPlacements.Card : LocalPlacements.Split;
        return new Option(placement, gpuLayers, blocks, cpuExpertLayers, 0, 0);
    }

    /// <summary>Tokens per second: the weights read per token from RAM and from the card at their measured speeds.</summary>
    private static double Speed(LocalModelProfile p, Option o, MemoryHeadroom room)
    {
        var share = p.IsMixtureOfExperts ? p.ActiveExpertShare : 0;
        var expertActive = p.ExpertBytesPerLayer * share;
        var ramBlocks = p.Layers - o.CardBlocks;
        double ramBytes = (ramBlocks * (p.LayerBytes + expertActive)) + (o.CpuExpertLayers * expertActive);
        double cardBytes = (o.CardBlocks * p.LayerBytes) + ((o.CardBlocks - o.CpuExpertLayers) * expertActive);
        if (o.GpuLayers > o.CardBlocks)
        {
            cardBytes += p.OutputBytes;
        }
        else
        {
            ramBytes += p.OutputBytes;
        }

        var handOvers = o.Placement switch
        {
            LocalPlacements.Experts => 2 * o.CpuExpertLayers,
            LocalPlacements.Split => 4,
            _ => 0,
        };
        var seconds = (ramBytes / room.RamBytesPerSecond) + (cardBytes / room.CardBytesPerSecond) + (handOvers * room.HandOverSeconds);
        return seconds > 0 ? 1 / seconds : 0;
    }

    /// <summary>How much more an earlier load took than planned, 1.0–1.5 (never less than the estimate).</summary>
    private static double Scale(long? measured, long? planned) =>
        measured is > 0 && planned is > 0 ? Math.Clamp((double)measured.Value / planned.Value, 1.0, 1.5) : 1.0;

    private static string Mb(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{bytes / (1024 * 1024)} MB");

    private sealed record Option(string Placement, int GpuLayers, int CardBlocks, int CpuExpertLayers, long Vram, long Ram);
}
