using Memento.Core.Engines;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Engines;

/// <summary>The sentences about the card's memory (DESIGN.md §17: the thing, the amount, what is safe, the fix).</summary>
public sealed class GpuMemoryWordingTests
{
    private const long Mib = 1024L * 1024;

    private static GpuInfo Card(long freeMib, params GpuMemoryHolder[] holders) =>
        FakeResourceProbe.Rtx with { DedicatedVideoMemoryBytes = 5994 * Mib, FreeVramBytes = freeMib * Mib, Holders = holders };

    [Fact]
    public void TheOwnersCaseNamesOllamaTheAmountsAndTheFix()
    {
        var gpu = Card(820, new GpuMemoryHolder("llama-server.exe", "Ollama (llama-server.exe)", 5120 * Mib));

        Assert.Equal(
            "The graphics card has 0.8 GB of 6 GB free. Ollama (llama-server.exe) is using 5.0 GB. Qwen3.5 4B needs 3.6 GB on the card, "
            + "so it runs on the processor until that memory is free. To use the card, close Ollama (llama-server.exe) or wait until it lets go of the memory, then check again.",
            GpuMemoryWording.Shortfall(gpu, "Qwen3.5 4B", 3686 * Mib, "it runs on the processor"));
    }

    [Fact]
    public void TheSummaryNamesTheTwoLargestHoldersAndOffersTheLauncherToClose()
    {
        var gpu = Card(
            640,
            new GpuMemoryHolder("llama-server.exe", "Ollama (llama-server.exe, started by Dictation)", 3158 * Mib) { StartedBy = "Dictation" },
            new GpuMemoryHolder("python.exe", "Python (python.exe, started by Dictation)", 1203 * Mib) { StartedBy = "Dictation" },
            new GpuMemoryHolder("dwm.exe", "Windows desktop (dwm.exe)", 374 * Mib) { IsWindows = true });

        Assert.Equal(
            "The graphics card has 0.6 GB of 6 GB free. Ollama (llama-server.exe, started by Dictation) is using 3.1 GB and Python (python.exe, started by Dictation) 1.2 GB.",
            GpuMemoryWording.Summary(gpu));
        Assert.EndsWith("To use the card, close Dictation or wait until it lets go of the memory, then check again.", GpuMemoryWording.Shortfall(gpu, "Large v3 Turbo", 2560 * Mib, "it transcribes on the processor (slower)"), StringComparison.Ordinal);
    }

    [Fact]
    public void SmallHoldersAreNotNamedAndWindowsIsNeverOfferedForClosing()
    {
        var gpu = Card(
            900,
            new GpuMemoryHolder("dwm.exe", "Windows desktop (dwm.exe)", 4600 * Mib) { IsWindows = true },
            new GpuMemoryHolder("Game.exe", "A Game (Game.exe)", 200 * Mib));

        Assert.Equal("The graphics card has 0.9 GB of 6 GB free. Windows desktop (dwm.exe) is using 4.5 GB.", GpuMemoryWording.Summary(gpu));
        Assert.EndsWith("so it runs on the processor until that memory is free.", GpuMemoryWording.Shortfall(gpu, "Qwen3.5 4B", 3686 * Mib, "it runs on the processor"), StringComparison.Ordinal);
    }

    [Fact]
    public void MementosOwnJobSaysItWillPass()
    {
        var gpu = Card(500, new GpuMemoryHolder("Memento.Worker.exe", "Memento", 3100 * Mib) { IsMemento = true });

        Assert.Equal(
            "The graphics card has 0.5 GB of 6 GB free. Memento is using 3.0 GB. Large v3 Turbo needs 2.5 GB on the card, so it transcribes on the processor (slower) until that memory is free. "
            + "Memento's own transcription or document job is using it; this changes when that job finishes.",
            GpuMemoryWording.Shortfall(gpu, "Large v3 Turbo", 2560 * Mib, "it transcribes on the processor (slower)"));
    }

    [Fact]
    public void WithoutHoldersOrAFreeReadingTheSentenceStillNamesTheCard()
    {
        Assert.Equal("The graphics card has 1.0 GB of 6 GB free.", GpuMemoryWording.Summary(Card(1024)));
        Assert.Equal(
            "The graphics card has 6 GB; how much is free could not be read.",
            GpuMemoryWording.Summary(FakeResourceProbe.Rtx with { FreeVramBytes = null }));
    }

    [Theory]
    [InlineData(5994, "6 GB")]
    [InlineData(8192, "8 GB")]
    [InlineData(3994, "4 GB")]
    [InlineData(1536, "1.5 GB")]
    public void TheCardIsNamedByTheSizeItIsSoldWith(long mib, string text) =>
        Assert.Equal(text, GpuMemoryWording.CardSize(mib * Mib));

    [Theory]
    [InlineData(103, "0.1 GB")]
    [InlineData(50, "under 0.1 GB")]
    [InlineData(0, "under 0.1 GB")]
    [InlineData(5120, "5.0 GB")]
    public void AmountsKeepOneDecimalSoALittleNeverReadsAsNothing(long mib, string text) =>
        Assert.Equal(text, GpuMemoryWording.Gb(mib * Mib));
}
