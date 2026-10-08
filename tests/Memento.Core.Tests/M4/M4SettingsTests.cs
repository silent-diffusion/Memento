using System.Text.Json;
using Memento.Core.Ai;
using Memento.Core.Bridge;
using Memento.Core.Engines;
using Memento.Core.Models;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.M4;

/// <summary>The M4 settings: default provider, local model, cloud models, default template and style (BRIDGE.md M4).</summary>
public sealed class M4SettingsTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task TheDefaultsAreTheBuiltInsAndTheModelForThisHardware()
    {
        var settings = await _host.ResultAsync("settings.get");
        var ai = settings.GetProperty("ai");

        Assert.Equal(JsonValueKind.Null, ai.GetProperty("defaultProviderId").ValueKind);
        Assert.Equal("ministral-3-3b-q4", ai.GetProperty("localModelId").GetString());
        Assert.False(ai.GetProperty("localModelChosen").GetBoolean());
        Assert.Equal(AiModelDefaults.AnthropicModel, ai.GetProperty("providers").GetProperty("anthropic").GetProperty("model").GetString());
        Assert.Equal(["claude-opus-5-5", "claude-fable-5-1"], ai.GetProperty("providers").GetProperty("anthropic").GetProperty("models").EnumerateArray().Select(m => m.GetString()));
        Assert.Equal(AiModelDefaults.OpenAiModel, ai.GetProperty("providers").GetProperty("openai").GetProperty("model").GetString());
        Assert.Equal("""{"defaultTemplateId":"meeting-minutes","defaultStyleId":"corporate"}""", settings.GetProperty("documents").GetRawText());
    }

    [Fact]
    public async Task EachM4FieldChangesAndNullGoesBackToTheDefault()
    {
        var set = await _host.ResultAsync("settings.set", """{"ai":{"defaultProviderId":"local","providers":{"anthropic":{"model":"claude-fable-5-1"}}},"documents":{"defaultTemplateId":"my-minutes","defaultStyleId":"academic"}}""");

        Assert.Equal("local", set.GetProperty("ai").GetProperty("defaultProviderId").GetString());
        Assert.Equal("claude-fable-5-1", set.GetProperty("ai").GetProperty("providers").GetProperty("anthropic").GetProperty("model").GetString());
        Assert.Equal("gpt-6-astra", set.GetProperty("ai").GetProperty("providers").GetProperty("openai").GetProperty("model").GetString());
        Assert.Equal("my-minutes", set.GetProperty("documents").GetProperty("defaultTemplateId").GetString());
        Assert.Equal("local", _host.Settings.Current.Ai.DefaultProviderId);

        var cleared = await _host.ResultAsync("settings.set", """{"ai":{"defaultProviderId":null,"providers":{"anthropic":{"model":null}}},"documents":{"defaultStyleId":null}}""");

        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("ai").GetProperty("defaultProviderId").ValueKind);
        Assert.Equal(AiModelDefaults.AnthropicModel, cleared.GetProperty("ai").GetProperty("providers").GetProperty("anthropic").GetProperty("model").GetString());
        Assert.Equal("my-minutes", cleared.GetProperty("documents").GetProperty("defaultTemplateId").GetString());
        Assert.Equal("corporate", cleared.GetProperty("documents").GetProperty("defaultStyleId").GetString());
        Assert.Null(_host.Settings.Current.Ai.Providers.Anthropic.Model);
    }

    [Theory]
    [InlineData("""{"ai":{"defaultProviderId":"gemini"}}""", "ai.defaultProviderId")]
    [InlineData("""{"ai":{"defaultProviderId":3}}""", "ai.defaultProviderId")]
    [InlineData("""{"ai":{"localModelId":"whisper-small"}}""", "ai.localModelId")]
    [InlineData("""{"ai":{"localModelId":"qwen3.5-4b-q4"}}""", "ai.localModelId")]
    [InlineData("""{"ai":{"providers":{"openai":{"model":"gpt 6 <x>"}}}}""", "ai.providers.openai.model")]
    [InlineData("""{"documents":{"defaultTemplateId":"../x"}}""", "documents.defaultTemplateId")]
    public async Task ABadValueNamesItsFieldAndChangesNothing(string patch, string field)
    {
        var before = await File.ReadAllTextAsync(_host.SettingsFile).ContinueWith(t => t.IsFaulted ? string.Empty : t.Result, TaskScheduler.Default);

        var response = await _host.CallAsync("settings.set", """{"theme":"dark",""" + patch[1..]);

        var error = response.GetProperty("error");
        Assert.Equal(DomainErrorCodes.SettingsInvalidValue, error.GetProperty("code").GetString());
        Assert.Equal(field, error.GetProperty("detail").GetString());
        Assert.Equal("system", _host.Settings.Current.Theme);
        Assert.Equal(before, File.Exists(_host.SettingsFile) ? await File.ReadAllTextAsync(_host.SettingsFile) : string.Empty);
    }

    [Fact]
    public void TheGraphicsCardModelIsRecommendedOnlyWhenItFits()
    {
        var catalog = ModelCatalog.Default;
        static ResourceSnapshot With(long? free) =>
            new(free is null ? [] : [new GpuInfo(0, "GPU", 0x10DE, 6L << 30, free, IsDiscrete: true)], null, 16, 16L << 30, 8L << 30);

        Assert.Equal("qwen3.5-4b-q4", LocalModelChoice.RecommendedId(catalog, With(5L << 30)));
        Assert.Equal("ministral-3-3b-q4", LocalModelChoice.RecommendedId(catalog, With(2L << 30)));
        Assert.Equal("ministral-3-3b-q4", LocalModelChoice.RecommendedId(catalog, With(null)));
        Assert.Equal("qwen3.5-4b-q4", LocalModelChoice.EffectiveId("qwen3.5-4b-q4", catalog, With(null), _ => true));
        Assert.Equal("ministral-3-3b-q4", LocalModelChoice.EffectiveId("whisper-small", catalog, With(null), _ => false));
    }
}
