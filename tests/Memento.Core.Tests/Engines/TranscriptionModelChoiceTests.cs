using Memento.Core.Engines;
using Memento.Core.Models;

namespace Memento.Core.Tests.Engines;

/// <summary>
/// The transcription model in effect for each mix of installed models, saved choice and free video memory: an installed
/// model whenever one is installed, so a PC with only Large v3 Turbo never waits for Small while the card is busy.
/// </summary>
public sealed class TranscriptionModelChoiceTests
{
    private const string Turbo = "whisper-large-v3-turbo";
    private const string Medium = "whisper-medium";
    private const string Small = "whisper-small";
    private const string Base = "whisper-base";
    private const long Plenty = 5L << 30;
    private const long Low = 2L << 30;

    private static ResourceSnapshot With(long? free) =>
        new(free is null ? [] : [new GpuInfo(0, "GPU", 0x10DE, 6L << 30, free, IsDiscrete: true)], null, 16, 16L << 30, 8L << 30);

    private static Func<string, bool> Installed(params string[] ids) => id => ids.Contains(id, StringComparer.Ordinal);

    private static string Recommended(long? free) =>
        free is { } f && f >= (ModelCatalog.Default.Find(Turbo)!.MinVramBytes ?? 0) ? Turbo : Small;

    [Theory]
    // A choice made in Settings is kept, installed or not (the stage waits for it and says so).
    [InlineData(new[] { Turbo, Small }, Turbo, Low, Turbo)]
    [InlineData(new[] { Turbo, Small }, Medium, Low, Medium)]
    [InlineData(new[] { Turbo }, Small, Low, Small)]
    [InlineData(new[] { Small }, Turbo, Plenty, Turbo)]
    [InlineData(new string[0], Medium, Low, Medium)]
    // Only Turbo installed, nothing chosen: Turbo, whatever the card has free (it runs on the processor when it does not fit).
    [InlineData(new[] { Turbo }, null, Plenty, Turbo)]
    [InlineData(new[] { Turbo }, null, Low, Turbo)]
    [InlineData(new[] { Turbo }, null, null, Turbo)]
    // Only Small installed: Small, also on a card with room for Turbo.
    [InlineData(new[] { Small }, null, Plenty, Small)]
    // Several installed without the recommendation: the most accurate one that fits the card, else the most accurate.
    [InlineData(new[] { Medium, Base }, null, Low, Medium)]
    [InlineData(new[] { Medium, Base }, null, 1L << 30, Base)]
    [InlineData(new[] { Turbo, Medium }, null, 1L << 30, Turbo)]
    // The recommendation installed: the recommendation.
    [InlineData(new[] { Turbo, Small }, null, Plenty, Turbo)]
    [InlineData(new[] { Turbo, Small }, null, Low, Small)]
    // None installed and nothing chosen: the recommendation (the one to download).
    [InlineData(new string[0], null, Plenty, Turbo)]
    [InlineData(new string[0], null, Low, Small)]
    public void TheModelInEffectIsAnInstalledOneWheneverOneIsInstalled(string[] installed, string? chosen, long? free, string expected)
    {
        Assert.Equal(expected, TranscriptionModelChoice.EffectiveId(chosen, Recommended(free), ModelCatalog.Default, With(free), Installed(installed)));
    }

    [Fact]
    public void ASavedChoiceThatIsNotATranscriptionModelIsIgnored()
    {
        Assert.Equal(Turbo, TranscriptionModelChoice.EffectiveId("qwen3.5-4b-q4", Recommended(Low), ModelCatalog.Default, With(Low), Installed(Turbo, "qwen3.5-4b-q4")));
    }
}
