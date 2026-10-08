using Memento.Core.Ai;
using Memento.Core.Engines;
using Memento.Core.Models;

namespace Memento.Core.Tests.M4;

/// <summary>
/// The local model in effect for each mix of installed models, saved choice and free video memory: an installed model
/// whenever one is installed, so a PC with only Qwen3.5 4B (or only Ministral 3 3B) never reads as "not installed".
/// </summary>
public sealed class LocalModelChoiceTests
{
    private const string Qwen = "qwen3.5-4b-q4";
    private const string Ministral = "ministral-3-3b-q4";
    private const long Plenty = 5L << 30;
    private const long Low = 2L << 30;

    private static ResourceSnapshot With(long? free) =>
        new(free is null ? [] : [new GpuInfo(0, "GPU", 0x10DE, 6L << 30, free, IsDiscrete: true)], null, 16, 16L << 30, 8L << 30);

    private static Func<string, bool> Installed(params string[] ids) => id => ids.Contains(id, StringComparer.Ordinal);

    [Theory]
    // Only Qwen installed: Qwen, whatever the card has free (it runs on the processor when it does not fit).
    [InlineData(new[] { Qwen }, null, Plenty, Qwen)]
    [InlineData(new[] { Qwen }, null, Low, Qwen)]
    [InlineData(new[] { Qwen }, null, null, Qwen)]
    [InlineData(new[] { Qwen }, Ministral, Low, Qwen)]
    // Only Ministral installed: Ministral, also on a card with room for Qwen.
    [InlineData(new[] { Ministral }, null, Plenty, Ministral)]
    [InlineData(new[] { Ministral }, null, Low, Ministral)]
    [InlineData(new[] { Ministral }, Qwen, Plenty, Ministral)]
    // Both: the saved choice, else the hardware's recommendation.
    [InlineData(new[] { Qwen, Ministral }, null, Plenty, Qwen)]
    [InlineData(new[] { Qwen, Ministral }, null, Low, Ministral)]
    [InlineData(new[] { Qwen, Ministral }, Qwen, Low, Qwen)]
    [InlineData(new[] { Qwen, Ministral }, Ministral, Plenty, Ministral)]
    // None: the saved choice, else the recommendation (the one to download).
    [InlineData(new string[0], null, Plenty, Qwen)]
    [InlineData(new string[0], null, Low, Ministral)]
    [InlineData(new string[0], Qwen, Low, Qwen)]
    public void TheModelInEffectIsAnInstalledOneWheneverOneIsInstalled(string[] installed, string? chosen, long? free, string expected)
    {
        Assert.Equal(expected, LocalModelChoice.EffectiveId(chosen, ModelCatalog.Default, With(free), Installed(installed)));
    }

    [Fact]
    public void ASavedChoiceThatIsNotALocalModelIsIgnored()
    {
        Assert.Equal(Qwen, LocalModelChoice.EffectiveId("whisper-small", ModelCatalog.Default, With(Low), Installed(Qwen, "whisper-small")));
    }

    [Fact]
    public void TheGraphicsCardModelFitsOnlyWithItsMinimumFree()
    {
        var qwen = ModelCatalog.Default.Find(Qwen)!;
        Assert.True(LocalModelChoice.FitsOnGpu(qwen, With(Plenty)));
        Assert.False(LocalModelChoice.FitsOnGpu(qwen, With(Low)));
        Assert.False(LocalModelChoice.FitsOnGpu(qwen, With(null)));
    }
}
