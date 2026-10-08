using System.Text.Json;
using Memento.AI;
using Memento.AI.Local;
using Memento.Core.Tests.Fakes;
using Memento.Generation.Tests.Support;

namespace Memento.Generation.Tests.Bridge;

/// <summary>
/// The local provider's readiness (<c>providers.list</c>) and the Settings snapshot's model for each mix of installed
/// local models and free video memory: Local is ready whenever a local model is installed, and both name the model that
/// will write and where it runs.
/// </summary>
public sealed class LocalReadinessTests : IDisposable
{
    private const string Qwen = LocalModelCatalog.Qwen35FourB;
    private const string Ministral = LocalModelCatalog.Ministral3ThreeB;
    private const long Plenty = 5L << 30;
    private const long Low = 2L << 30;

    private readonly M4Host _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<JsonElement> LocalAsync()
    {
        var list = await _host.ResultAsync("providers.list", new { });
        return list.GetProperty("providers").EnumerateArray().Single(p => p.GetProperty("id").GetString() == "local");
    }

    private async Task<string?> SettingsModelAsync() =>
        (await _host.ResultAsync("settings.get", new { })).GetProperty("ai").GetProperty("localModelId").GetString();

    [Theory]
    [InlineData(new[] { Qwen }, Plenty, Qwen, "Qwen3.5 4B · graphics card")]
    [InlineData(new[] { Qwen }, Low, Qwen, "Qwen3.5 4B · processor")]
    [InlineData(new[] { Ministral }, Plenty, Ministral, "Ministral 3 3B · graphics card")]
    [InlineData(new[] { Ministral }, Low, Ministral, "Ministral 3 3B · processor")]
    [InlineData(new[] { Qwen, Ministral }, Plenty, Qwen, "Qwen3.5 4B · graphics card")]
    [InlineData(new[] { Qwen, Ministral }, Low, Ministral, "Ministral 3 3B · processor")]
    public async Task LocalIsReadyWithTheInstalledModelAndNamesWhereItRuns(string[] installed, long free, string model, string label)
    {
        foreach (var id in installed)
        {
            _host.InstallLocalModel(id);
        }

        _host.Host.Probe.Snapshot = FakeResourceProbe.WithGpu(free);

        var local = await LocalAsync();

        Assert.True(local.GetProperty("ready").GetBoolean(), local.ToString());
        Assert.Equal(model, local.GetProperty("modelId").GetString());
        Assert.Equal(label, local.GetProperty("modelLabel").GetString());
        Assert.Equal(model, await SettingsModelAsync());
        Assert.DoesNotContain("not installed", local.GetProperty("detail").GetString()!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task OnlyQwenWithTheCardBusyRunsQwenOnTheProcessorAndSaysWhy()
    {
        _host.InstallLocalModel(Qwen);
        _host.Host.Probe.Snapshot = FakeResourceProbe.WithGpu(Low);

        var local = await LocalAsync();

        Assert.True(local.GetProperty("ready").GetBoolean());
        var detail = local.GetProperty("detail").GetString()!;
        Assert.Contains("The graphics card has 2.0 GB free and Qwen3.5 4B needs", detail, StringComparison.Ordinal);
        Assert.Contains("so it runs on the processor", detail, StringComparison.Ordinal);
        Assert.Contains("Runs on the processor with an 8k context. Nothing leaves this PC.", detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AChoiceThatIsNotInstalledAnyMoreFallsBackToTheInstalledModelAndSaysSo()
    {
        _host.InstallLocalModel(Ministral);
        await _host.ResultAsync("settings.set", new { ai = new { localModelId = Ministral } });
        File.Delete(_host.Host.Models.PathOf(M4Host.Catalog.Find(Ministral)!));
        _host.InstallLocalModel(Qwen);
        _host.Host.Probe.Snapshot = FakeResourceProbe.WithGpu(Plenty);

        var local = await LocalAsync();
        var settings = (await _host.ResultAsync("settings.get", new { })).GetProperty("ai");

        Assert.True(local.GetProperty("ready").GetBoolean());
        Assert.Equal(Qwen, local.GetProperty("modelId").GetString());
        Assert.StartsWith("Ministral 3 3B, chosen in Settings, is not installed, so Qwen3.5 4B writes the documents.", local.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(Qwen, settings.GetProperty("localModelId").GetString());
        Assert.False(settings.GetProperty("localModelChosen").GetBoolean());
    }

    [Fact]
    public async Task ChosenQwenWithTheCardBusyAndMinistralInstalledWritesWithMinistralButKeepsTheChoice()
    {
        _host.InstallLocalModel(Qwen);
        _host.InstallLocalModel(Ministral);
        await _host.ResultAsync("settings.set", new { ai = new { localModelId = Qwen } });
        _host.Host.Probe.Snapshot = FakeResourceProbe.WithGpu(Low);

        var local = await LocalAsync();
        var settings = (await _host.ResultAsync("settings.get", new { })).GetProperty("ai");

        Assert.Equal(Ministral, local.GetProperty("modelId").GetString());
        Assert.Contains("so Ministral 3 3B writes instead this time", local.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Equal(Qwen, settings.GetProperty("localModelId").GetString());
        Assert.True(settings.GetProperty("localModelChosen").GetBoolean());
    }

    [Theory]
    [InlineData(Plenty, "Qwen3.5 4B")]
    [InlineData(Low, "Ministral 3 3B")]
    public async Task WithNoLocalModelInstalledLocalNamesTheOneToDownload(long free, string name)
    {
        _host.Host.Probe.Snapshot = FakeResourceProbe.WithGpu(free);

        var local = await LocalAsync();

        Assert.False(local.GetProperty("ready").GetBoolean());
        Assert.Equal(AiErrorCodes.ModelNotInstalled, local.GetProperty("code").GetString());
        Assert.Contains($"The local model {name} is not installed.", local.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AGenerationWithOnlyQwenAndTheCardBusyLoadsItOnTheProcessor()
    {
        _host.InstallLocalModel(Qwen);
        _host.Host.Probe.Snapshot = FakeResourceProbe.WithGpu(Low);
        var id = await _host.CreateMeetingAsync();

        var start = await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync("local") });
        var final = await _host.FinishedAsync(start.GetProperty("jobId").GetString()!);

        Assert.Equal("done", final.GetProperty("stage").GetString());
        Assert.Equal(LocalLlmDevices.Cpu, Assert.Single(_host.Providers.Local).Device);
    }
}
