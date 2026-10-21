using System.Text.Json;
using Memento.AI;
using Memento.AI.Local;
using Memento.Core.Bridge;
using Memento.Core.Engines;
using Memento.Core.Tests.Fakes;
using Memento.Generation.Generation;
using Memento.Generation.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Memento.Generation.Tests.Bridge;

/// <summary>
/// The larger models through the bridge (2.2, BRIDGE.md "Larger models"): <c>providers.list</c> names the placement and
/// lists every larger model's plan, a model that does not fit is not ready with the §17 sentence and no other model stands
/// in, the generation gets the placement, a waiting transcription keeps the card, and a recording that starts while RAM
/// is short unloads the model with History saying so.
/// </summary>
public sealed class LargerModelTests : IDisposable
{
    private const long MiB = 1024 * 1024;
    private const long GiB = 1024 * MiB;
    private const string Moe = LocalModelCatalog.Qwen35MoE;

    private readonly FakeMemoryDemands _demands = new();
    private readonly M4Host _host;

    public LargerModelTests()
    {
        _host = new M4Host(services => services.Replace(ServiceDescriptor.Singleton<IMemoryDemands>(_demands)));
        Pc(24 * GiB);
    }

    public void Dispose() => _host.Dispose();

    private void Pc(long availableRam, long freeVram = 5222 * MiB) =>
        _host.Host.Probe.Snapshot = FakeResourceProbe.WithGpu(freeVram) with { TotalRamBytes = 40 * GiB, AvailableRamBytes = availableRam, LogicalProcessors = 16 };

    private async Task<JsonElement> LocalAsync() =>
        (await _host.ResultAsync("providers.list", new { })).GetProperty("providers").EnumerateArray().Single(p => p.GetProperty("id").GetString() == "local");

    private async Task ChooseAsync(string id)
    {
        _host.InstallLocalModel(id);
        await _host.ResultAsync("settings.set", new { ai = new { localModelId = id } });
    }

    [Fact]
    public async Task AFittingLargerModelNamesWherePartsRunAndEveryLargerModelHasItsLine()
    {
        await ChooseAsync(Moe);

        var local = await LocalAsync();

        Assert.True(local.GetProperty("ready").GetBoolean(), local.ToString());
        Assert.Equal(Moe, local.GetProperty("modelId").GetString());
        Assert.Equal("Qwen3.5 35B-A3B · graphics card and RAM", local.GetProperty("modelLabel").GetString());
        var plan = local.GetProperty("memoryPlan");
        Assert.True(plan.GetProperty("fits").GetBoolean());
        Assert.Equal("experts", plan.GetProperty("placement").GetString());
        Assert.Equal(41, plan.GetProperty("gpuLayers").GetInt32());
        Assert.Equal(37, plan.GetProperty("cpuExpertLayers").GetInt32());
        Assert.Equal(40, plan.GetProperty("totalLayers").GetInt32());
        Assert.Equal("Runs on the graphics card with the experts of 37 of 40 layers in RAM · about 12 tokens/s · 18 GB of RAM while writing", plan.GetProperty("line").GetString());
        Assert.Equal(plan.GetProperty("line").GetString() + ", with a 16k context. Nothing leaves this PC.", local.GetProperty("detail").GetString());
        var larger = local.GetProperty("largerModels").EnumerateArray().ToList();
        Assert.Equal([LocalModelCatalog.Qwen35TwentySevenB, Moe], larger.Select(p => p.GetProperty("modelId").GetString()));
        Assert.All(larger, p => Assert.False(string.IsNullOrWhiteSpace(p.GetProperty("line").GetString())));
        var models = (await _host.ResultAsync("models.list", new { })).GetProperty("models").EnumerateArray().ToList();
        Assert.Equal("larger", models.Single(m => m.GetProperty("id").GetString() == Moe).GetProperty("group").GetString());
        Assert.Equal(JsonValueKind.Null, models.Single(m => m.GetProperty("id").GetString() == LocalModelCatalog.Qwen35FourB).GetProperty("group").ValueKind);
    }

    [Fact]
    public async Task ALargerModelThatDoesNotFitIsNotReadyAndNoSmallerModelStandsIn()
    {
        _host.InstallLocalModel(LocalModelCatalog.Qwen35FourB);
        await ChooseAsync(Moe);
        Pc(8 * GiB);

        var local = await LocalAsync();

        Assert.False(local.GetProperty("ready").GetBoolean());
        Assert.Equal(AiErrorCodes.NotEnoughMemory, local.GetProperty("code").GetString());
        Assert.Equal("Not enough memory", local.GetProperty("reason").GetString());
        Assert.Equal(Moe, local.GetProperty("modelId").GetString());
        Assert.Matches(@"^Qwen3\.5 35B-A3B needs \d+ GB of memory; this PC has \d+ GB free\. Close other programs or choose a smaller model\.$", local.GetProperty("detail").GetString());
        Assert.False(local.GetProperty("memoryPlan").GetProperty("fits").GetBoolean());

        var id = await _host.CreateMeetingAsync();
        var refused = await _host.ErrorAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync() });
        Assert.Equal(DomainErrorCodes.AiProviderNotReady, refused.GetProperty("code").GetString());
        Assert.Equal(AiErrorCodes.NotEnoughMemory, refused.GetProperty("detail").GetString());
        Assert.Equal(0, _host.Providers.LocalCreated);
    }

    [Fact]
    public async Task TheGenerationGetsThePlannedPlacement()
    {
        await ChooseAsync(Moe);
        var id = await _host.CreateMeetingAsync();

        var done = await _host.FinishedAsync((await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync() })).GetProperty("jobId").GetString()!);

        Assert.Equal("done", done.GetProperty("stage").GetString());
        var options = Assert.Single(_host.Providers.Local);
        Assert.Equal(LocalLlmDevices.Gpu, options.Device);
        Assert.Equal(16384, options.ContextTokens);
        Assert.Equal(8, options.Threads);
        Assert.True(options.FlashAttention);
        Assert.NotNull(options.Placement);
        Assert.Equal(41, options.Placement!.GpuLayers);
        Assert.Equal(37, options.Placement.CpuExpertLayers);
        Assert.Equal(MemoryHeadroom.Default.OsBytes + MemoryHeadroom.Default.AppBytes, options.Placement.RamReserveBytes);
    }

    [Fact]
    public async Task AWaitingTranscriptionKeepsTheCardWhenThereIsRamForTheModel()
    {
        await ChooseAsync(Moe);
        Pc(30 * GiB);
        _demands.Value = new MemoryDemands(false, false, true, 2560 * MiB, 512 * MiB);

        var plan = (await LocalAsync()).GetProperty("memoryPlan");

        Assert.Equal("ram", plan.GetProperty("placement").GetString());
        Assert.Equal(0, plan.GetProperty("vramBytes").GetInt64());
        Assert.StartsWith("Runs in RAM on the processor", plan.GetProperty("line").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARecordingThatStartsWhileRamIsShortUnloadsTheModelAndHistorySaysSo()
    {
        await ChooseAsync(Moe);
        var id = await _host.CreateMeetingAsync();
        var blocking = new BlockingLocalProvider();
        _host.Providers.Make = _ => blocking;
        var job = (await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync() })).GetProperty("jobId").GetString()!;
        await blocking.Started.Task.WaitAsync(Patience.Ceiling);
        var generation = _host.Get<GenerationService>();

        // Enough RAM for capture: nothing happens.
        Pc(6 * GiB);
        generation.OnRecordingStarted(withVideo: false);
        await Task.Delay(100);
        Assert.False(blocking.Cancelled);

        Pc((long)(1.5 * GiB));
        generation.OnRecordingStarted(withVideo: false);
        var final = await _host.FinishedAsync(job);

        Assert.Equal("failed", final.GetProperty("stage").GetString());
        Assert.Equal(AiErrorCodes.NotEnoughMemory, final.GetProperty("code").GetString());
        Assert.StartsWith("Qwen3.5 35B-A3B was unloaded because a recording started and this PC had 1.5 GB of memory free", final.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Contains("The recording is not affected.", final.GetProperty("message").GetString(), StringComparison.Ordinal);
        var history = await _host.Host.Store.ReadHistoryAsync(id, CancellationToken.None);
        Assert.Contains(history, h => h.Event == "failed" && h.Stage == "minutes" && h.Summary.Contains("unloaded for a recording", StringComparison.Ordinal));
        Assert.Empty((await _host.ResultAsync("documents.list", new { recordingId = id })).GetProperty("documents").EnumerateArray());
    }

    [Fact]
    public async Task ASmallModelIsNeverUnloadedForARecording()
    {
        await ChooseAsync(LocalModelCatalog.Qwen35FourB);
        var id = await _host.CreateMeetingAsync();
        var blocking = new BlockingLocalProvider();
        _host.Providers.Make = _ => blocking;
        var job = (await _host.ResultAsync("generation.start", new { recordingId = id, template = await _host.MeetingMinutesAsync() })).GetProperty("jobId").GetString()!;
        await blocking.Started.Task.WaitAsync(Patience.Ceiling);

        Pc(GiB);
        _host.Get<GenerationService>().OnRecordingStarted(withVideo: true);
        await Task.Delay(100);

        Assert.False(blocking.Cancelled);
        await _host.ResultAsync("generation.cancel", new { jobId = job });
        Assert.Equal("cancelled", (await _host.FinishedAsync(job)).GetProperty("stage").GetString());
    }

    private sealed class FakeMemoryDemands : IMemoryDemands
    {
        public MemoryDemands Value { get; set; } = MemoryDemands.None;

        public MemoryDemands Current() => Value;
    }

    private sealed class BlockingLocalProvider : IAiProvider
    {
        private readonly MeetingProvider _shape = new();

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public bool Cancelled { get; private set; }

        public string Id => _shape.Id;

        public string DisplayName => _shape.DisplayName;

        public AiProviderKind Kind => _shape.Kind;

        public string Model => _shape.Model;

        public AiCapabilities Capabilities => _shape.Capabilities;

        public int CountTokens(string text) => _shape.CountTokens(text);

        public Task<AiReadiness> CheckAsync(CancellationToken cancellationToken) => Task.FromResult(AiReadiness.Ready());

        public async Task<AiResponse> GenerateAsync(AiRequest request, IProgress<AiProgress>? progress, CancellationToken cancellationToken)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled = true;
                throw;
            }

            throw new InvalidOperationException("unreachable");
        }
    }
}
