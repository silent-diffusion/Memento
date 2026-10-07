using System.Text.Json;
using Memento.Core.Settings;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Settings;

/// <summary>Settings › Transcription, Speakers and History: defaults, partial updates, validation, normalisation.</summary>
public sealed class M2SettingsTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<string> ErrorAsync(string parameters)
    {
        var response = await _host.CallAsync("settings.set", parameters);
        return response.GetProperty("error").GetProperty("code").GetString()!;
    }

    [Fact]
    public async Task DefaultsFollowTheContract()
    {
        var settings = await _host.ResultAsync("settings.get");

        Assert.Equal(
            """{"auto":true,"timing":"after","pauseWhenBusy":true,"modelId":"small","cpuFallbackModelId":"small","language":"auto","keepWordTimestamps":true,"lowConfidenceThreshold":0.5}""",
            settings.GetProperty("transcription").GetRawText());
        Assert.Equal("""{"identify":true,"expectedSpeakers":"auto","rememberRenamed":false,"embeddingModelId":"nemo-titanet-small"}""", settings.GetProperty("speakers").GetRawText());
        Assert.Equal("""{"keepVersions":true,"keepDays":90}""", settings.GetProperty("history").GetRawText());
    }

    [Fact]
    public async Task TheRecommendedModelFollowsTheHardwareUntilOneIsChosen()
    {
        _host.Probe.Snapshot = FakeResourceProbe.WithGpu(5L << 30);
        Assert.Equal("large-v3-turbo", (await _host.ResultAsync("settings.get")).GetProperty("transcription").GetProperty("modelId").GetString());

        await _host.ResultAsync("settings.set", """{"transcription":{"modelId":"medium"}}""");
        _host.Probe.Snapshot = FakeResourceProbe.WithGpu(0);

        Assert.Equal("medium", (await _host.ResultAsync("settings.get")).GetProperty("transcription").GetProperty("modelId").GetString());
    }

    [Fact]
    public async Task M2BlocksChangeOnlyTheFieldsTheyCarry()
    {
        var result = await _host.ResultAsync("settings.set", """{"transcription":{"language":"de","lowConfidenceThreshold":0.6},"speakers":{"expectedSpeakers":3},"history":{"keepDays":30}}""");

        var transcription = result.GetProperty("transcription");
        Assert.Equal("de", transcription.GetProperty("language").GetString());
        Assert.Equal(0.6, transcription.GetProperty("lowConfidenceThreshold").GetDouble());
        Assert.True(transcription.GetProperty("auto").GetBoolean());
        Assert.Equal(3, result.GetProperty("speakers").GetProperty("expectedSpeakers").GetInt32());
        Assert.True(result.GetProperty("speakers").GetProperty("identify").GetBoolean());
        Assert.Equal(30, result.GetProperty("history").GetProperty("keepDays").GetInt32());

        var back = await _host.ResultAsync("settings.set", """{"speakers":{"expectedSpeakers":"auto"}}""");
        Assert.Equal("auto", back.GetProperty("speakers").GetProperty("expectedSpeakers").GetString());
        Assert.Null(_host.Settings.Current.Speakers.ExpectedSpeakers);
    }

    [Theory]
    [InlineData("""{"transcription":{"timing":"sometimes"}}""")]
    [InlineData("""{"transcription":{"language":"english"}}""")]
    [InlineData("""{"transcription":{"lowConfidenceThreshold":1.5}}""")]
    [InlineData("""{"transcription":{"modelId":"nemo-titanet-small"}}""")]
    [InlineData("""{"transcription":{"cpuFallbackModelId":"gpt"}}""")]
    [InlineData("""{"speakers":{"expectedSpeakers":0}}""")]
    [InlineData("""{"speakers":{"expectedSpeakers":"two"}}""")]
    [InlineData("""{"speakers":{"embeddingModelId":"small"}}""")]
    [InlineData("""{"history":{"keepDays":0}}""")]
    public async Task InvalidValuesChangeNothing(string parameters)
    {
        var before = File.Exists(_host.SettingsFile) ? await File.ReadAllTextAsync(_host.SettingsFile) : null;

        Assert.Equal("settings.invalidValue", await ErrorAsync(parameters));

        Assert.Equal(before, File.Exists(_host.SettingsFile) ? await File.ReadAllTextAsync(_host.SettingsFile) : null);
    }

    [Fact]
    public async Task AnM1SettingsFileReadsWithTheM2Defaults()
    {
        using var directory = new TempDirectory();
        var path = directory.File("settings.json");
        await File.WriteAllTextAsync(path, """{"schemaVersion":1,"theme":"dark","recording":{"defaultType":"lecture"}}""");
        using var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal(new TranscriptionSettings().Timing, settings.Transcription.Timing);
        Assert.True(settings.Speakers.Identify);
        Assert.Equal(90, settings.History.KeepDays);
    }

    [Fact]
    public async Task OutOfRangeValuesOnDiskFallBackToDefaults()
    {
        using var directory = new TempDirectory();
        var path = directory.File("settings.json");
        await File.WriteAllTextAsync(path, """{"schemaVersion":1,"transcription":{"timing":"x","language":"Klingon","lowConfidenceThreshold":7,"modelId":"small"},"speakers":{"expectedSpeakers":99},"history":{"keepDays":-1}}""");
        using var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("after", settings.Transcription.Timing);
        Assert.Equal("auto", settings.Transcription.Language);
        Assert.Equal(0.5, settings.Transcription.LowConfidenceThreshold);
        Assert.Equal("small", settings.Transcription.ModelId);
        Assert.Null(settings.Speakers.ExpectedSpeakers);
        Assert.Equal(90, settings.History.KeepDays);
    }

    [Fact]
    public async Task UnknownFieldsInTheNewBlocksSurviveAWrite()
    {
        using var directory = new TempDirectory();
        var path = directory.File("settings.json");
        await File.WriteAllTextAsync(path, """{"schemaVersion":1,"transcription":{"futureKnob":42}}""");
        using var store = new JsonSettingsStore(path, NullLogger<JsonSettingsStore>.Instance);
        await store.LoadAsync(CancellationToken.None);

        await store.UpdateAsync(s => s with { Theme = "dark" }, CancellationToken.None);

        using var written = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(42, written.RootElement.GetProperty("transcription").GetProperty("futureKnob").GetInt32());
    }
}
