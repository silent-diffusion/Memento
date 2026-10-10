using System.Text.Json;
using Memento.Core.Bridge.Methods;
using Memento.Core.Settings;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Settings;

public sealed class RecordingSettingsTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static string? ErrorCode(JsonElement response) =>
        response.TryGetProperty("error", out var error) ? error.GetProperty("code").GetString() : null;

    [Fact]
    public void DefaultsMatchTheContract()
    {
        var recording = new AppSettings().Recording;

        Assert.Equal("meeting", recording.DefaultType);
        Assert.Empty(recording.DefaultSourceIds);
        Assert.True(recording.KeepSeparateTracks);
        Assert.Equal("flac", recording.Storage.Codec);
        Assert.Null(recording.Storage.BitrateKbps);
        Assert.Equal(30, recording.CheckpointSeconds);
        Assert.Equal(10, recording.LowSpaceGb);
        Assert.Equal(10L * 1024 * 1024 * 1024, recording.LowSpaceThresholdBytes);
        Assert.Null(recording.Validate());
        Assert.Equal(2, AppSettings.CurrentSchemaVersion);
    }

    [Fact]
    public async Task AnM0SettingsFileReadsWithRecordingDefaults()
    {
        await File.WriteAllTextAsync(_host.SettingsFile, """{"schemaVersion":1,"theme":"dark","listDensity":"compact"}""");
        using var store = new JsonSettingsStore(_host.SettingsFile, NullLogger<JsonSettingsStore>.Instance);

        var settings = await store.LoadAsync(CancellationToken.None);

        Assert.Equal("dark", settings.Theme);
        Assert.Equal(30, settings.Recording.CheckpointSeconds);
        Assert.Equal("flac", settings.Recording.Storage.Codec);
    }

    [Fact]
    public async Task OutOfRangeValuesOnDiskFallBackToDefaultsOneByOne()
    {
        await File.WriteAllTextAsync(
            _host.SettingsFile,
            """{"schemaVersion":1,"recording":{"checkpointSeconds":1,"lowSpaceGb":20,"keepSeparateTracks":false,"storage":{"codec":"ogg"},"defaultType":"interview","futureKnob":7}}""");
        using var store = new JsonSettingsStore(_host.SettingsFile, NullLogger<JsonSettingsStore>.Instance);

        var recording = (await store.LoadAsync(CancellationToken.None)).Recording;
        await store.UpdateAsync(s => s with { Theme = "light" }, CancellationToken.None);

        Assert.Equal(30, recording.CheckpointSeconds);
        Assert.Equal(20, recording.LowSpaceGb);
        Assert.True(recording.KeepSeparateTracks);
        Assert.Equal("flac", recording.Storage.Codec);
        Assert.Equal("interview", recording.DefaultType);
        Assert.Contains("\"futureKnob\": 7", await File.ReadAllTextAsync(_host.SettingsFile), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"checkpointSeconds":5}""", true)]
    [InlineData("""{"checkpointSeconds":300}""", true)]
    [InlineData("""{"checkpointSeconds":4}""", false)]
    [InlineData("""{"checkpointSeconds":301}""", false)]
    [InlineData("""{"lowSpaceGb":1}""", true)]
    [InlineData("""{"lowSpaceGb":500}""", true)]
    [InlineData("""{"lowSpaceGb":0}""", false)]
    [InlineData("""{"lowSpaceGb":501}""", false)]
    [InlineData("""{"storage":{"codec":"mp3","bitrateKbps":64}}""", true)]
    [InlineData("""{"storage":{"codec":"aac","bitrateKbps":320}}""", true)]
    [InlineData("""{"storage":{"codec":"aac","bitrateKbps":63}}""", false)]
    [InlineData("""{"storage":{"codec":"mp3","bitrateKbps":321}}""", false)]
    [InlineData("""{"storage":{"codec":"flac","bitrateKbps":9999}}""", true)]
    [InlineData("""{"storage":{"codec":"ogg"}}""", false)]
    [InlineData("""{"keepSeparateTracks":false}""", false)]
    [InlineData("""{"defaultType":""}""", false)]
    public async Task RecordingValuesAreValidated(string recording, bool accepted)
    {
        var response = await _host.CallAsync("settings.set", $$"""{"recording":{{recording}}}""");

        if (accepted)
        {
            Assert.Null(ErrorCode(response));
        }
        else
        {
            Assert.Equal(SettingsSetMethod.InvalidValueCode, ErrorCode(response));
            Assert.EndsWith("Nothing was changed.", response.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
            Assert.Equal(30, _host.Settings.Current.Recording.CheckpointSeconds);
        }
    }

    [Fact]
    public async Task TheRecordingBlockIsReplacedWhole()
    {
        await _host.ResultAsync("settings.set", """{"recording":{"checkpointSeconds":60,"lowSpaceGb":20,"defaultSourceIds":["mic:a"]}}""");

        // The UI always sends the full block; a field it leaves out takes its default, not its old value.
        var snapshot = await _host.ResultAsync("settings.set", """{"theme":"dark","recording":{"lowSpaceGb":25}}""");

        var recording = snapshot.GetProperty("recording");
        Assert.Equal("dark", snapshot.GetProperty("theme").GetString());
        Assert.Equal(25, recording.GetProperty("lowSpaceGb").GetInt32());
        Assert.Equal(30, recording.GetProperty("checkpointSeconds").GetInt32());
        Assert.Empty(recording.GetProperty("defaultSourceIds").EnumerateArray());

        // Without a recording block, the stored one is untouched.
        var themeOnly = await _host.ResultAsync("settings.set", """{"theme":"light"}""");
        Assert.Equal(25, themeOnly.GetProperty("recording").GetProperty("lowSpaceGb").GetInt32());
    }

    [Fact]
    public async Task LossyCodecsGetADefaultBitrateAndFlacNone()
    {
        var aac = await _host.ResultAsync("settings.set", """{"recording":{"storage":{"codec":"aac","downmixMono":true}}}""");
        var flac = await _host.ResultAsync("settings.set", """{"recording":{"storage":{"codec":"flac","bitrateKbps":256}}}""");

        Assert.Equal(192, aac.GetProperty("recording").GetProperty("storage").GetProperty("bitrateKbps").GetInt32());
        Assert.True(aac.GetProperty("recording").GetProperty("storage").GetProperty("downmixMono").GetBoolean());
        Assert.Equal(JsonValueKind.Null, flac.GetProperty("recording").GetProperty("storage").GetProperty("bitrateKbps").ValueKind);
    }

    [Fact]
    public async Task TheLowSpaceThresholdDrivesTheFooter()
    {
        _host.FreeSpace.FreeBytes = 15L * 1024 * 1024 * 1024;
        Assert.False((await _host.ResultAsync("status.get")).GetProperty("storage").GetProperty("lowSpace").GetBoolean());

        await _host.ResultAsync("settings.set", """{"recording":{"lowSpaceGb":20}}""");

        Assert.True((await _host.ResultAsync("status.get")).GetProperty("storage").GetProperty("lowSpace").GetBoolean());
    }
}
