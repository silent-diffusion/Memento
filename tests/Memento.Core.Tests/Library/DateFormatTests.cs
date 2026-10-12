using System.Globalization;
using System.Text.Json;
using Memento.Core.Formatting;
using Memento.Core.Tests.Fakes;
using static Memento.Core.Tests.Fakes.TestRecordings;

namespace Memento.Core.Tests.Library;

/// <summary>2.1: Settings › General › Date format, its presets, and the dated default title.</summary>
public sealed class DateFormatTests : IDisposable
{
    private static readonly DateTimeOffset Day = new(2026, 2, 16, 9, 30, 0, TimeSpan.FromHours(1));

    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Theory]
    [InlineData("yearMonthDay", "2026 February 16")]
    [InlineData("dayMonthYear", "16 February 2026")]
    [InlineData("monthDayYear", "February 16, 2026")]
    [InlineData("iso", "2026-02-16")]
    [InlineData("something else", "2026 February 16")]
    [InlineData(null, "2026 February 16")]
    public void EachPresetReadsAsDesigned(string? format, string expected) => Assert.Equal(expected, DateFormats.Format(Day, format));

    [Fact]
    public void WindowsSettingFollowsTheRegionsLongDate()
    {
        Assert.Equal("Monday, 16 February 2026", DateFormats.Format(Day, DateFormats.Windows, CultureInfo.GetCultureInfo("en-GB")));
        Assert.Equal("Monday, February 16, 2026", DateFormats.Format(Day, DateFormats.Windows, CultureInfo.GetCultureInfo("en-US")));
    }

    [Fact]
    public void TheDefaultTitleCarriesTheDateAndTheType()
    {
        Assert.Equal("Untitled meeting · 2026 February 16", DateFormats.UntitledTitle("meeting", Day, DateFormats.Default));
        Assert.Equal("Untitled interview · 16 February 2026", DateFormats.UntitledTitle("interview", Day, DateFormats.DayMonthYear));
        Assert.Equal(["yearMonthDay", "dayMonthYear", "monthDayYear", "iso", "windows"], DateFormats.All);
    }

    [Fact]
    public async Task TheSettingIsInGeneralAndRefusesUnknownPresets()
    {
        var general = (await _host.ResultAsync("settings.get")).GetProperty("general");
        Assert.Equal("yearMonthDay", general.GetProperty("dateFormat").GetString());

        var changed = await _host.ResultAsync("settings.set", """{"general":{"dateFormat":"iso"}}""");
        Assert.Equal("iso", changed.GetProperty("general").GetProperty("dateFormat").GetString());
        Assert.Equal("iso", _host.Settings.Current.General.DateFormat);

        var kept = await _host.ResultAsync("settings.set", """{"general":{"keepRunningInTray":true}}""");
        Assert.Equal("iso", kept.GetProperty("general").GetProperty("dateFormat").GetString());

        var refused = await _host.CallAsync("settings.set", """{"general":{"dateFormat":"dd/mm"}}""");
        Assert.Equal("settings.invalidValue", refused.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("iso", _host.Settings.Current.General.DateFormat);

        // The Library's Folders / Timeline switch is remembered beside it.
        Assert.Equal("timeline", general.GetProperty("libraryView").GetString());
        var view = await _host.ResultAsync("settings.set", """{"general":{"libraryView":"folders"}}""");
        Assert.Equal("folders", view.GetProperty("general").GetProperty("libraryView").GetString());
        var badView = await _host.CallAsync("settings.set", """{"general":{"libraryView":"grid"}}""");
        Assert.Equal("settings.invalidValue", badView.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task ANewRecordingWithoutATitleIsDatedInTheChosenFormatAndATypedTitleIsKept()
    {
        await _host.ResultAsync("settings.set", """{"general":{"dateFormat":"iso"}}""");
        var result = await _host.ResultAsync("recording.start", JsonSerializer.Serialize(new { title = "  ", type = "interview", sourceIds = new[] { Mic } }));
        var recordingId = result.GetProperty("recordingId").GetString()!;
        var manifest = await _host.Store.LoadAsync(recordingId, CancellationToken.None);

        Assert.Equal($"Untitled interview · {manifest.CreatedAt:yyyy-MM-dd}", manifest.Details.Title);
        await _host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId = result.GetProperty("sessionId").GetString() }));
        await _host.Recordings.WhenIdleAsync();

        // Changing the format later leaves existing titles alone.
        await _host.ResultAsync("settings.set", """{"general":{"dateFormat":"dayMonthYear"}}""");
        Assert.StartsWith("Untitled interview · " + manifest.CreatedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), (await _host.Store.LoadAsync(recordingId, CancellationToken.None)).Details.Title, StringComparison.Ordinal);
        var typed = await _host.RecordAsync("Weekly sync", 1);
        Assert.Equal("Weekly sync", (await _host.Store.LoadAsync(typed, CancellationToken.None)).Details.Title);
    }
}
