using Memento.Core.Ai;
using Memento.Core.Settings;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Settings;

/// <summary>
/// The model chosen for each cloud provider (Settings › AI and privacy): the lists and their defaults, a model id typed
/// by hand, and how a settings file without one, or with an empty one, is read.
/// </summary>
public sealed class CloudModelSettingsTests
{
    [Fact]
    public void EachProviderListsItsModelsWithTheDefaultFirst()
    {
        Assert.Equal(["claude-opus-5-5", "claude-fable-5-1", "claude-sonnet-5-5", "claude-haiku-5-5"], AiModelDefaults.AnthropicModels);
        Assert.Equal(["gpt-6-astra", "gpt-6.1-sol", "gpt-6-luna"], AiModelDefaults.OpenAiModels);
        Assert.Equal("claude-opus-5-5", AiModelDefaults.Default("anthropic"));
        Assert.Equal("gpt-6-astra", AiModelDefaults.Default("openai"));
        Assert.All(AiModelDefaults.AnthropicCatalog.Concat(AiModelDefaults.OpenAiCatalog), m =>
        {
            Assert.True(m.Listed);
            Assert.True(m.SendsEffort);
            Assert.False(string.IsNullOrWhiteSpace(m.Note));
            Assert.EndsWith(".", m.Note, StringComparison.Ordinal);
            Assert.True(AiModelDefaults.IsValidModelId(m.Id));
        });
        Assert.Contains("top tier", AiModelDefaults.Describe("anthropic", "claude-fable-5-1").Note, StringComparison.Ordinal);
        Assert.False(AiModelDefaults.Describe("anthropic", "claude-haiku-5-5").ServerSideFallback);
        Assert.True(AiModelDefaults.Describe("anthropic", "claude-opus-5-5").ServerSideFallback);
    }

    [Theory]
    [InlineData(null, "claude-opus-5-5")]
    [InlineData("", "claude-opus-5-5")]
    [InlineData("   ", "claude-opus-5-5")]
    [InlineData("claude haiku", "claude-opus-5-5")]
    [InlineData("claude-haiku-5-5", "claude-haiku-5-5")]
    [InlineData("claude-opus-4-8", "claude-opus-4-8")]
    public void TheModelInEffectIsTheSavedOneOrTheDefault(string? saved, string expected) =>
        Assert.Equal(expected, AiModelDefaults.Effective("anthropic", saved));

    [Theory]
    [InlineData("claude-opus-4-8", true)]
    [InlineData("gpt-6.1-sol", true)]
    [InlineData("anthropic.claude-opus-5-5", true)]
    [InlineData("ft:gpt-6-luna:org:custom:abc123", true)]
    [InlineData("models/claude_x", true)]
    [InlineData("", false)]
    [InlineData("-leading-dash", false)]
    [InlineData("has space", false)]
    [InlineData("quote'd", false)]
    [InlineData("<script>", false)]
    [InlineData("ünicode", false)]
    public void AModelIdTypedByHandMustBePlausible(string id, bool valid)
    {
        Assert.Equal(valid, AiModelDefaults.IsValidModelId(id));
    }

    [Fact]
    public void AModelIdIsAtMostAHundredCharacters()
    {
        Assert.True(AiModelDefaults.IsValidModelId(new string('a', 100)));
        Assert.False(AiModelDefaults.IsValidModelId(new string('a', 101)));
    }

    [Fact]
    public void AModelNotInTheListIsSentAsTypedWithAConservativeProfile()
    {
        var claude = AiModelDefaults.Describe("anthropic", "claude-opus-4-8");
        var chatGpt = AiModelDefaults.Describe("openai", "gpt-4o");

        Assert.Equal(("claude-opus-4-8", false, false, false, 200_000, 32_000), (claude.Id, claude.Listed, claude.SendsEffort, claude.ServerSideFallback, claude.ContextTokens, claude.MaxOutputTokens));
        Assert.Equal(("gpt-4o", false, false, 128_000, 16_384), (chatGpt.Id, chatGpt.Listed, chatGpt.SendsEffort, chatGpt.ContextTokens, chatGpt.MaxOutputTokens));
        Assert.Equal("Not in Memento's list. Memento assumes it reads up to 200k tokens a request and sends it no effort setting.", claude.Note);
        Assert.Throws<ArgumentOutOfRangeException>(() => AiModelDefaults.Describe("local", "x"));
    }

    [Theory]
    [InlineData(1_000_000, "1M")]
    [InlineData(1_050_000, "1.05M")]
    [InlineData(200_000, "200k")]
    [InlineData(128_000, "128k")]
    public void TokenSizesReadShort(int tokens, string text) => Assert.Equal(text, AiModelDefaults.Tokens(tokens));

    [Fact]
    public async Task AFileWithoutModelsReadsAsTheDefaults()
    {
        using var directory = new Fakes.TempDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, """{ "schemaVersion": 2, "ai": { "enabled": true, "providers": null } }""");
        using var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.True(settings.Ai.Enabled);
        Assert.Null(settings.Ai.Providers.Anthropic.Model);
        Assert.Null(settings.Ai.Providers.Openai.Model);
    }

    [Fact]
    public async Task AnEmptyOrImplausibleSavedModelReadsAsTheDefaultAndIsNeverWrittenBack()
    {
        using var directory = new Fakes.TempDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, """{ "schemaVersion": 2, "ai": { "providers": { "anthropic": { "model": "" }, "openai": { "model": "gpt 6 <x>" } } } }""");
        using var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var settings = await store.LoadAsync(CancellationToken.None);
        await store.UpdateAsync(s => s with { Theme = ThemePreference.Dark }, CancellationToken.None);

        Assert.Null(settings.Ai.Providers.Anthropic.Model);
        Assert.Null(settings.Ai.Providers.Openai.Model);
        var written = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain("\"model\": \"\"", written, StringComparison.Ordinal);
        Assert.DoesNotContain("gpt 6", written, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AModelTypedByHandIsKeptAsTyped()
    {
        using var directory = new Fakes.TempDirectory();
        var path = Path.Combine(directory.Path, "settings.json");
        await File.WriteAllTextAsync(path, """{ "schemaVersion": 2, "ai": { "providers": { "anthropic": { "model": "claude-opus-4-8" } } } }""");
        using var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("claude-opus-4-8", settings.Ai.Providers.Anthropic.Model);
        Assert.Null(settings.Ai.Providers.Openai.Model);
    }
}
