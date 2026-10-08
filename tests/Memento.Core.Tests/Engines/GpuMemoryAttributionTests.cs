using Memento.Core.Engines;

namespace Memento.Core.Tests.Engines;

/// <summary>
/// Who holds the graphics card's memory, from fake <c>\GPU Process Memory(*)\Dedicated Usage</c> counters and a fake
/// process table shaped like the product owner's PC: an app that bundles Ollama, whose model server holds the card.
/// </summary>
public sealed class GpuMemoryAttributionTests
{
    private const long Mib = 1024L * 1024;
    private const int Self = 9000;
    private static readonly long Card = GpuMemoryAttribution.Luid(0x0000F961, 0);
    private static readonly long Igpu = GpuMemoryAttribution.Luid(0x0000E676, 0);
    private static readonly DateTime Boot = new(2026, 10, 8, 8, 0, 0, DateTimeKind.Utc);

    private sealed class FakeProcesses(params ProcessEntry[] entries) : IProcessDirectory
    {
        public List<int> Asked { get; } = [];

        public ProcessEntry? Find(int pid)
        {
            Asked.Add(pid);
            return entries.FirstOrDefault(e => e.Pid == pid);
        }
    }

    private static ProcessEntry P(int pid, int parent, string exe, string? friendly = null, int minute = 1) =>
        new(pid, parent, exe, friendly, Boot.AddMinutes(minute));

    /// <summary>The owner's PC: a dictation app started Ollama, which started llama.cpp's server, and a Python speech server.</summary>
    private static FakeProcesses OwnersPc() => new(
        P(4, 0, "System", minute: 0),
        P(800, 4, "explorer.exe", "Windows Explorer"),
        P(2052, 4, "dwm.exe", "Desktop Window Manager"),
        P(14496, 800, "DictationApp.exe", "Dictation", minute: 2),
        P(4596, 14496, "ollama.exe", minute: 3),
        P(5192, 4596, "llama-server.exe", minute: 4),
        P(20420, 14496, "python.exe", "Python", minute: 3),
        P(Self, 800, "Memento.exe", "Memento", minute: 5),
        P(9100, Self, "msedgewebview2.exe", "Microsoft Edge WebView2", minute: 6),
        P(9200, Self, "Memento.Worker.exe", minute: 7));

    private static GpuProcessCounter C(int pid, long luid, long mib, int phys = 0) =>
        new($"pid_{pid}_luid_0x{(uint)(luid >> 32):x8}_0x{(uint)luid:x8}_phys_{phys}", mib * Mib);

    [Theory]
    [InlineData("pid_5192_luid_0x00000000_0x0000f961_phys_0", 5192, 0x0000F961L)]
    [InlineData("pid_20420_luid_0x00000000_0x0000F961_phys_1", 20420, 0x0000F961L)]
    [InlineData("pid_7_luid_0x00000001_0x00000002_phys_0", 7, 0x0000000100000002L)]
    [InlineData("PID_7_LUID_0x00000000_0x0000e676_PHYS_0", 7, 0x0000E676L)]
    public void ReadsTheProcessAndAdapterFromAnInstanceName(string instance, int pid, long luid)
    {
        Assert.True(GpuMemoryAttribution.TryParseInstance(instance, out var readPid, out var readLuid));
        Assert.Equal(pid, readPid);
        Assert.Equal(luid, readLuid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("luid_0x00000000_0x0000f961_phys_0")]
    [InlineData("pid__luid_0x00000000_0x0000f961_phys_0")]
    [InlineData("pid_-3_luid_0x00000000_0x0000f961_phys_0")]
    [InlineData("pid_12_luid_00000000_0x0000f961_phys_0")]
    [InlineData("pid_12_luid_0x00000000_0xZZ_phys_0")]
    [InlineData("pid_12_engtype_3D")]
    public void IgnoresInstanceNamesItCannotRead(string instance) =>
        Assert.False(GpuMemoryAttribution.TryParseInstance(instance, out _, out _));

    [Fact]
    public void SumsAProcessOverItsPhysicalAdaptersOnTheCardOnly()
    {
        var byProcess = GpuMemoryAttribution.ByProcess(
            [C(5192, Card, 3000), C(5192, Card, 158, phys: 1), C(5192, Igpu, 500), C(2052, Card, 374), C(2052, Igpu, 124), C(77, Card, 0), new("_Total", 9 * Mib)],
            Card);

        Assert.Equal(2, byProcess.Count);
        Assert.Equal(3158 * Mib, byProcess[5192]);
        Assert.Equal(374 * Mib, byProcess[2052]);
    }

    [Fact]
    public void NamesOllamaAndTheAppThatStartedItLargestFirst()
    {
        var processes = OwnersPc();
        var byProcess = GpuMemoryAttribution.ByProcess([C(5192, Card, 3158), C(20420, Card, 1203), C(2052, Card, 374), C(800, Card, 127)], Card);

        var holders = GpuMemoryAttribution.Holders(byProcess, processes, Self);

        Assert.Equal(
            ["Ollama (llama-server.exe, started by Dictation)", "Python (python.exe, started by Dictation)", "Windows desktop (dwm.exe)"],
            holders.Select(h => h.Description));
        Assert.Equal([3158 * Mib, 1203 * Mib, 374 * Mib], holders.Select(h => h.Bytes));
        Assert.Equal("llama-server.exe", holders[0].ProcessName);
        Assert.Equal("Dictation", holders[0].StartedBy);
        Assert.True(holders[2].IsWindows);
        Assert.All(holders, h => Assert.False(h.IsMemento));
    }

    [Fact]
    public void OllamaStartedFromItsOwnTrayAppNamesNoOtherApp()
    {
        var processes = new FakeProcesses(
            P(800, 4, "explorer.exe"),
            P(100, 800, "ollama app.exe", "Ollama", minute: 2),
            P(101, 100, "ollama.exe", minute: 3),
            P(102, 101, "llama-server.exe", minute: 4));

        var holder = GpuMemoryAttribution.Describe(processes.Find(102)!, processes, Self);

        Assert.Equal("Ollama (llama-server.exe)", holder.Description);
        Assert.Null(holder.StartedBy);
        Assert.Equal("Ollama (ollama.exe)", GpuMemoryAttribution.Describe(processes.Find(101)!, processes, Self).Description);
    }

    [Fact]
    public void LlamaCppsServerWithoutOllamaIsNamedForWhatItIs()
    {
        var processes = new FakeProcesses(P(800, 4, "explorer.exe"), P(300, 800, "Studio.exe", "Model Studio", minute: 2), P(301, 300, "llama-server.exe", minute: 3));

        Assert.Equal("llama.cpp server (llama-server.exe, started by Model Studio)", GpuMemoryAttribution.Describe(processes.Find(301)!, processes, Self).Description);
    }

    [Fact]
    public void MementoTheWebViewAndTheWorkerAreOneEntry()
    {
        var processes = OwnersPc();
        var byProcess = GpuMemoryAttribution.ByProcess([C(Self, Card, 60), C(9100, Card, 90), C(9200, Card, 2100), C(5192, Card, 1000)], Card);

        var holders = GpuMemoryAttribution.Holders(byProcess, processes, Self);

        Assert.Equal("Memento", holders[0].Description);
        Assert.True(holders[0].IsMemento);
        Assert.Equal(2250 * Mib, holders[0].Bytes);
        Assert.Equal("Memento.Worker.exe", holders[0].ProcessName);
        Assert.Equal("Ollama (llama-server.exe, started by Dictation)", holders[1].Description);
    }

    [Fact]
    public void AWorkerFromAnotherMementoProcessStillCountsAsMemento()
    {
        var processes = new FakeProcesses(P(500, 4, "Memento.Worker.exe"));

        Assert.True(GpuMemoryAttribution.Describe(processes.Find(500)!, processes, Self).IsMemento);
    }

    [Fact]
    public void ServersWithTheSameNameAreAddedTogether()
    {
        var processes = new FakeProcesses(P(800, 4, "explorer.exe"), P(100, 800, "ollama.exe", minute: 2), P(101, 100, "llama-server.exe", minute: 3), P(102, 100, "llama-server.exe", minute: 3));

        var holders = GpuMemoryAttribution.Holders(GpuMemoryAttribution.ByProcess([C(101, Card, 2000), C(102, Card, 1500)], Card), processes, Self);

        var only = Assert.Single(holders);
        Assert.Equal("Ollama (llama-server.exe)", only.Description);
        Assert.Equal(3500 * Mib, only.Bytes);
    }

    [Fact]
    public void AProcessThatHasExitedIsAnotherProgramAndSmallHoldersAreLeftOut()
    {
        var processes = OwnersPc();
        var byProcess = GpuMemoryAttribution.ByProcess([C(31337, Card, 700), C(5192, Card, 31), C(2052, Card, 40)], Card);

        var holders = GpuMemoryAttribution.Holders(byProcess, processes, Self);

        Assert.Equal(["Another program (process 31337)", "Windows desktop (dwm.exe)"], holders.Select(h => h.Description));
    }

    [Fact]
    public void AtMostThreeHoldersAndOnlyTheLargestFewProcessesAreLookedUp()
    {
        var processes = OwnersPc();
        var counters = Enumerable.Range(1, 40).Select(i => C(10_000 + i, Card, 40 + i)).Append(C(5192, Card, 3000));

        var holders = GpuMemoryAttribution.Holders(GpuMemoryAttribution.ByProcess(counters, Card), processes, Self);

        Assert.Equal(3, holders.Count);
        Assert.Equal("Ollama (llama-server.exe, started by Dictation)", holders[0].Description);
        Assert.True(processes.Asked.Distinct().Count() < 30, $"looked up {processes.Asked.Distinct().Count()} processes");
    }

    [Fact]
    public void AReusedParentIdIsNotTakenForTheLauncher()
    {
        // pid 14496 now belongs to a program that started after python.exe: Windows reused the id of the real parent.
        var processes = new FakeProcesses(P(14496, 800, "Game.exe", "A Game", minute: 30), P(20420, 14496, "python.exe", "Python", minute: 3));

        var holder = GpuMemoryAttribution.Describe(processes.Find(20420)!, processes, Self);

        Assert.Equal("Python (python.exe)", holder.Description);
        Assert.Null(holder.StartedBy);
    }

    [Fact]
    public void ARuntimeStartedFromAShellNamesNoLauncherAndAPlainExecutableKeepsItsFileName()
    {
        var processes = new FakeProcesses(
            P(800, 4, "explorer.exe"),
            P(810, 800, "WindowsTerminal.exe", minute: 2),
            P(811, 810, "pwsh.exe", minute: 2),
            P(812, 811, "python.exe", "Python", minute: 3),
            P(900, 800, "render.exe", "render", minute: 3),
            P(901, 800, "Editor.exe", "A video editor with a very long file description that goes on", minute: 3));

        Assert.Equal("Python (python.exe)", GpuMemoryAttribution.Describe(processes.Find(812)!, processes, Self).Description);
        Assert.Equal("render.exe", GpuMemoryAttribution.Describe(processes.Find(900)!, processes, Self).Description);
        Assert.Equal("Editor.exe", GpuMemoryAttribution.Describe(processes.Find(901)!, processes, Self).Description);
    }

    [Fact]
    public void AParentLoopEndsTheWalk()
    {
        var processes = new FakeProcesses(P(1, 2, "python.exe", minute: 3), P(2, 1, "node.exe", minute: 3));

        Assert.Equal("python.exe", GpuMemoryAttribution.Describe(processes.Find(1)!, processes, Self).Description);
    }
}
