using System.Globalization;

namespace Memento.AI.Local;

/// <summary>
/// The sentences Settings, the Builder, History and "How this was made" show about a plan (DESIGN.md §11 "Larger
/// models", §17): "Runs 28 of 64 layers on the graphics card, the rest in RAM · about 6 tokens/s · 18 GB of RAM while
/// writing", and when it does not fit "Qwen3.5 35B-A3B needs 26 GB of memory; this PC has 11 GB free. Close other
/// programs or choose a smaller model."
/// </summary>
public static class MemoryPlanWording
{
    private const double Gib = 1024.0 * 1024 * 1024;

    /// <summary>Where the model runs, how fast and how much RAM it takes, for a plan that fits.</summary>
    public static string Line(LocalMemoryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        var where = plan.Placement switch
        {
            LocalPlacements.Card => "Runs on the graphics card",
            LocalPlacements.Experts when plan.CpuExpertLayers >= plan.TotalLayers => "Runs on the graphics card with its experts in RAM",
            LocalPlacements.Experts => string.Create(CultureInfo.InvariantCulture, $"Runs on the graphics card with the experts of {plan.CpuExpertLayers} of {plan.TotalLayers} layers in RAM"),
            LocalPlacements.Split => string.Create(CultureInfo.InvariantCulture, $"Runs {plan.CardBlocks} of {plan.TotalLayers} layers on the graphics card, the rest in RAM"),
            LocalPlacements.Ram => "Runs in RAM on the processor",
            _ => "Does not fit in this PC's memory now",
        };
        var line = string.Create(CultureInfo.InvariantCulture, $"{where} · {Speed(plan.TokensPerSecond)} · {Gb(plan.RamBytes)} of RAM while writing");
        return plan.MeetingMinutes > 0 ? $"{line} · {Meeting(plan.MeetingMinutes)}" : line;
    }

    /// <summary>"minutes of a 20-minute meeting in about 18 minutes", "… in about 9 hours".</summary>
    public static string Meeting(double minutes) =>
        minutes >= 90
            ? string.Create(CultureInfo.InvariantCulture, $"minutes of a 20-minute meeting in about {Math.Round(minutes / 60):0} hours")
            : string.Create(CultureInfo.InvariantCulture, $"minutes of a 20-minute meeting in about {Math.Max(1, Math.Round(minutes)):0} minutes");

    /// <summary>
    /// What a load actually took, for "How this was made" and History: "Ran 28 of 64 layers on the graphics card, the rest
    /// in RAM · 6.2 tokens/s · 18 GB of RAM".
    /// </summary>
    public static string Loaded(LocalModelProfile profile, LocalLoadMeasurement load)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(load);
        var blocks = Math.Clamp(load.GpuLayers - 1, 0, profile.Layers);
        var where = load.GpuLayers <= 0 ? "Ran in RAM on the processor"
            : load.CpuExpertLayers >= profile.Layers ? "Ran on the graphics card with its experts in RAM"
            : load.CpuExpertLayers > 0 ? string.Create(CultureInfo.InvariantCulture, $"Ran on the graphics card with the experts of {load.CpuExpertLayers} of {profile.Layers} layers in RAM")
            : blocks >= profile.Layers ? "Ran on the graphics card"
            : string.Create(CultureInfo.InvariantCulture, $"Ran {blocks} of {profile.Layers} layers on the graphics card, the rest in RAM");
        var speed = load.TokensPerSecond > 0 ? string.Create(CultureInfo.InvariantCulture, $" · {load.TokensPerSecond:0.0} tokens/s") : string.Empty;
        var ram = load.RamBytes > 0 ? $" · {Gb(load.RamBytes)} of RAM" : string.Empty;
        return where + speed + ram;
    }

    /// <summary>"about 6 tokens/s"; one decimal under 2.</summary>
    public static string Speed(double tokensPerSecond) =>
        tokensPerSecond >= 2
            ? string.Create(CultureInfo.InvariantCulture, $"about {Math.Round(tokensPerSecond):0} tokens/s")
            : string.Create(CultureInfo.InvariantCulture, $"about {Math.Max(0.1, tokensPerSecond):0.0} tokens/s");

    /// <summary>The §17 sentence for a model that does not fit: the model, what it needs, what is free, the fix.</summary>
    public static string Refusal(string modelName, LocalMemoryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(modelName);
        ArgumentNullException.ThrowIfNull(plan);
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{modelName} needs {GbUp(plan.NeededBytes)} of memory; this PC has {GbDown(plan.FreeBytes)} free. Close other programs or choose a smaller model.");
    }

    /// <summary>"Graphics card + RAM", "graphics card", "processor": the short placement in a model label.</summary>
    public static string Where(LocalMemoryPlan plan)
    {
        ArgumentNullException.ThrowIfNull(plan);
        return plan.Placement switch
        {
            LocalPlacements.Card => "graphics card",
            LocalPlacements.Split or LocalPlacements.Experts => "graphics card and RAM",
            _ => "processor",
        };
    }

    /// <summary>"18 GB" (whole gigabytes from 10 GB, one decimal under).</summary>
    public static string Gb(long bytes) =>
        bytes >= 10 * Gib
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(bytes / Gib):0} GB")
            : string.Create(CultureInfo.InvariantCulture, $"{bytes / Gib:0.0} GB");

    private static string GbUp(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{Math.Ceiling(bytes / Gib):0} GB");

    private static string GbDown(long bytes) => string.Create(CultureInfo.InvariantCulture, $"{Math.Floor(Math.Max(0, bytes) / Gib):0} GB");
}
