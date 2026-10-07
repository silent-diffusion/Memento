using Memento.Core.Engines;
using Memento.Core.Models;
using Memento.Core.Settings;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Engines;

public sealed class EngineSelectorTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private EngineSelector Selector => _host.Get<EngineSelector>();

    [Fact]
    public void ADiscreteGpuWithEnoughFreeMemoryGetsLargeV3TurboOnTheGpu()
    {
        var snapshot = FakeResourceProbe.WithGpu(5L << 30);

        Assert.Equal("large-v3-turbo", Selector.RecommendedTranscriptionModelId(snapshot));
        var device = Selector.SelectDevice("large-v3-turbo", forceCpu: false, snapshot);
        Assert.True(device.UseGpu);
        Assert.Equal("GPU", device.Kind);
        Assert.Equal("local GPU", device.ProgressWord);
    }

    [Fact]
    public void TooLittleFreeVideoMemoryRecommendsSmallAndRunsTurboOnTheProcessorWithAReason()
    {
        var snapshot = FakeResourceProbe.WithGpu(2L << 30);

        Assert.Equal("small", Selector.RecommendedTranscriptionModelId(snapshot));
        var device = Selector.SelectDevice("large-v3-turbo", forceCpu: false, snapshot);
        Assert.False(device.UseGpu);
        Assert.Contains("2.0 GB of video memory free", device.Reason, StringComparison.Ordinal);
        Assert.True(Selector.SelectDevice("small", forceCpu: false, snapshot).UseGpu);
    }

    [Fact]
    public void NoGpuMeansSmallOnTheProcessor()
    {
        Assert.Equal("small", Selector.RecommendedTranscriptionModelId(ResourceSnapshot.Empty));
        Assert.False(Selector.SelectDevice("small", forceCpu: false, ResourceSnapshot.Empty).UseGpu);
    }

    [Fact]
    public void TheCpuRemedyForcesTheProcessor()
    {
        Assert.False(Selector.SelectDevice("small", forceCpu: true, FakeResourceProbe.WithGpu(5L << 30)).UseGpu);
    }

    [Fact]
    public void AnUnsetModelReadsAsTheRecommendedOneAndAChosenOneIsKept()
    {
        _host.Probe.Snapshot = FakeResourceProbe.WithGpu(5L << 30);

        Assert.Equal("large-v3-turbo", Selector.EffectiveModelId(new TranscriptionSettings()));
        Assert.Equal("medium", Selector.EffectiveModelId(new TranscriptionSettings { ModelId = "medium" }));
        Assert.Equal("large-v3-turbo", Selector.EffectiveModelId(new TranscriptionSettings { ModelId = "pyannote-segmentation-3-0" }));
    }

    [Fact]
    public void SpeakerAndOcrModelsAreRecommendedWhenTheCatalogSaysAny()
    {
        var snapshot = ResourceSnapshot.Empty;

        Assert.True(Selector.IsRecommended(ModelCatalog.Default.Find("nemo-titanet-small")!, snapshot));
        Assert.False(Selector.IsRecommended(ModelCatalog.Default.Find("3dspeaker-eres2net-base")!, snapshot));
        Assert.True(Selector.IsRecommended(ModelCatalog.Default.Find("small")!, snapshot));
        Assert.False(Selector.IsRecommended(ModelCatalog.Default.Find("large-v3-turbo")!, snapshot));
    }
}
