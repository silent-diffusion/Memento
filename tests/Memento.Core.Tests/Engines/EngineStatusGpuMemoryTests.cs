using System.Text.Json;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Core.Tests.Engines;

/// <summary>
/// <c>engine.status</c> and <c>engine.refresh</c> name who holds the graphics card's memory and say why transcription
/// runs on the processor (the product owner's report: Ollama held the card and Settings only said "0.1 GB free").
/// </summary>
public sealed class EngineStatusGpuMemoryTests : IDisposable
{
    private readonly BridgeTestHost _host = new(configure: services => services.AddSingleton(TestCatalogs.Tiny));

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task EngineStatusNamesWhoHoldsTheCardAndWhyTranscriptionRunsOnTheProcessor()
    {
        TestCatalogs.Install(_host, "whisper-large-v3-turbo");
        _host.Probe.Snapshot = FakeResourceProbe.WithGpu(820L << 20, FakeResourceProbe.Ollama);

        var transcription = (await _host.ResultAsync("engine.status")).GetProperty("transcription");

        Assert.Equal("CPU", transcription.GetProperty("device").GetString());
        Assert.Equal("whisper-large-v3-turbo", transcription.GetProperty("model").GetString());
        var memory = transcription.GetProperty("gpuMemory");
        Assert.Equal(820L << 20, memory.GetProperty("freeBytes").GetInt64());
        Assert.Equal(6L << 30, memory.GetProperty("totalBytes").GetInt64());
        var holder = Assert.Single(memory.GetProperty("holders").EnumerateArray());
        Assert.Equal("Ollama (llama-server.exe)", holder.GetProperty("description").GetString());
        Assert.Equal("The graphics card has 0.8 GB of 6 GB free. Ollama (llama-server.exe) is using 5.0 GB.", memory.GetProperty("summary").GetString());
        Assert.Equal(
            "The graphics card has 0.8 GB of 6 GB free. Ollama (llama-server.exe) is using 5.0 GB. Large v3 Turbo needs 2.5 GB on the card, so it transcribes on the processor (slower) until that memory is free. "
            + "To use the card, close Ollama (llama-server.exe) or wait until it lets go of the memory, then check again.",
            transcription.GetProperty("note").GetString());
    }

    [Fact]
    public async Task ATranscriptionThatFitsOnTheCardHasNoNoteAndTheFooterCarriesNoCardDetail()
    {
        TestCatalogs.Install(_host, "whisper-large-v3-turbo");
        _host.Probe.Snapshot = FakeResourceProbe.WithGpu(5L << 30, FakeResourceProbe.Ollama with { Bytes = 300L << 20 });

        var transcription = (await _host.ResultAsync("engine.status")).GetProperty("transcription");
        var footer = (await _host.ResultAsync("status.get")).GetProperty("engine").GetProperty("detail");

        Assert.Equal("GPU", transcription.GetProperty("device").GetString());
        Assert.Equal(JsonValueKind.Null, transcription.GetProperty("note").ValueKind);
        Assert.Equal(JsonValueKind.Object, transcription.GetProperty("gpuMemory").ValueKind);
        Assert.Equal(JsonValueKind.Null, footer.GetProperty("gpuMemory").ValueKind);
        Assert.Equal(JsonValueKind.Null, footer.GetProperty("note").ValueKind);
    }

    [Fact]
    public async Task CheckAgainReadsTheCardAgainAndAnswersLikeEngineStatus()
    {
        TestCatalogs.Install(_host, "whisper-large-v3-turbo");
        _host.Probe.Snapshot = FakeResourceProbe.WithGpu(820L << 20, FakeResourceProbe.Ollama);
        var before = await _host.ResultAsync("engine.refresh");

        // The owner closes Ollama and checks again.
        _host.Probe.Snapshot = FakeResourceProbe.WithGpu(5L << 30);
        var after = await _host.ResultAsync("engine.refresh");

        Assert.Equal(2, _host.Probe.Refreshes);
        Assert.Equal("transcription,speakers", string.Join(",", after.EnumerateObject().Select(p => p.Name)));
        Assert.Equal("CPU", before.GetProperty("transcription").GetProperty("device").GetString());
        Assert.Equal("GPU", after.GetProperty("transcription").GetProperty("device").GetString());
        Assert.Equal(5L << 30, after.GetProperty("transcription").GetProperty("freeVramBytes").GetInt64());
        Assert.Empty(after.GetProperty("transcription").GetProperty("gpuMemory").GetProperty("holders").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("transcription").GetProperty("note").ValueKind);
    }

    [Fact]
    public async Task CheckAgainTakesNoParametersAndAPcWithoutACardHasNoCardMemory()
    {
        var transcription = (await _host.ResultAsync("engine.refresh")).GetProperty("transcription");

        Assert.Equal(JsonValueKind.Null, transcription.GetProperty("gpuMemory").ValueKind);
        Assert.Equal(JsonValueKind.Null, transcription.GetProperty("note").ValueKind);
        Assert.Equal("bridge.invalidParams", (await _host.CallAsync("engine.refresh", """{"force":true}""")).GetProperty("error").GetProperty("code").GetString());
    }
}
