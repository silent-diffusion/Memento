using System.Globalization;

namespace Memento.Core.Engines;

/// <summary>
/// The sentences Settings and the Builder show about the graphics card's memory (DESIGN.md §17: name the thing, the
/// amount, what is safe, the fix): "The graphics card has 0.8 GB of 6 GB free. Ollama (llama-server.exe) is using 5.0 GB."
/// and, when a model does not fit, "… Qwen3.5 4B needs 3.6 GB on the card, so it runs on the processor until that memory
/// is free."
/// </summary>
public static class GpuMemoryWording
{
    /// <summary>Holders named in a sentence: the two largest that hold at least this much.</summary>
    public const long NamedHolderBytes = 256L * 1024 * 1024;

    private const double Gib = 1024.0 * 1024 * 1024;

    /// <summary>"The graphics card has 0.8 GB of 6 GB free." plus who holds the rest, when known.</summary>
    public static string Summary(GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        var free = gpu.FreeVramBytes is { } f
            ? string.Create(CultureInfo.InvariantCulture, $"The graphics card has {Gb(f)} of {CardSize(gpu.DedicatedVideoMemoryBytes)} free.")
            : string.Create(CultureInfo.InvariantCulture, $"The graphics card has {CardSize(gpu.DedicatedVideoMemoryBytes)}; how much is free could not be read.");
        return HoldersSentence(gpu) is { } holders ? $"{free} {holders}" : free;
    }

    /// <summary>
    /// Why <paramref name="modelName"/> does not run on the card and what happens instead, e.g. "The graphics card has 0.8 GB
    /// of 6 GB free. Ollama (llama-server.exe) is using 5.0 GB. Qwen3.5 4B needs 3.6 GB on the card, so it runs on the
    /// processor until that memory is free. To use the card, close Ollama (llama-server.exe) or wait until it lets go of
    /// the memory, then check again." The program offered for closing is the app that started a runtime when known
    /// ("close Dictation"), never a part of Windows or Memento itself.
    /// </summary>
    /// <param name="instead">What happens meanwhile, e.g. "it runs on the processor" or "Ministral 3 3B writes instead".</param>
    public static string Shortfall(GpuInfo gpu, string modelName, long neededBytes, string instead)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        var text = string.Create(
            CultureInfo.InvariantCulture,
            $"{Summary(gpu)} {modelName} needs {Gb(neededBytes)} on the card, so {instead} until that memory is free.");
        var other = Named(gpu).FirstOrDefault(h => !h.IsMemento && !h.IsWindows);
        if (other is not null)
        {
            return $"{text} To use the card, close {other.StartedBy ?? other.Description} or wait until it lets go of the memory, then check again.";
        }

        return Named(gpu).Any(h => h.IsMemento)
            ? $"{text} Memento's own transcription or document job is using it; this changes when that job finishes."
            : text;
    }

    /// <summary>"Ollama (llama-server.exe) is using 3.1 GB and Python (python.exe) 1.2 GB.", or <c>null</c> when nobody holds much.</summary>
    public static string? HoldersSentence(GpuInfo gpu)
    {
        ArgumentNullException.ThrowIfNull(gpu);
        var named = Named(gpu);
        return named.Count switch
        {
            0 => null,
            1 => $"{Subject(named[0])} is using {Gb(named[0].Bytes)}.",
            _ => $"{Subject(named[0])} is using {Gb(named[0].Bytes)} and {Subject(named[1])} {Gb(named[1].Bytes)}.",
        };
    }

    /// <summary>"0.8 GB", "5.0 GB", "under 0.1 GB": one decimal, so a card with little free memory never reads as "0 GB".</summary>
    public static string Gb(long bytes) =>
        bytes < 0.1 * Gib ? "under 0.1 GB" : string.Create(CultureInfo.InvariantCulture, $"{bytes / Gib:0.0} GB");

    /// <summary>The card's size as it is sold: "6 GB" for the 5.9 GB DXGI reports (the driver keeps a little); one decimal under 2 GB.</summary>
    public static string CardSize(long bytes) =>
        bytes >= 2 * Gib
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(bytes / Gib, MidpointRounding.AwayFromZero):0} GB")
            : Gb(bytes);

    private static List<GpuMemoryHolder> Named(GpuInfo gpu) =>
        gpu.Holders.Where(h => h.Bytes >= NamedHolderBytes).OrderByDescending(h => h.Bytes).Take(2).ToList();

    private static string Subject(GpuMemoryHolder holder) => holder.IsMemento ? GpuMemoryAttribution.MementoName : holder.Description;
}
