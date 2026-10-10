using System.Globalization;
using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;
using Memento.Core.Maintenance;
using Memento.Core.Tests.M3;

namespace Memento.Core.Tests.Host;

/// <summary>Settings › General › Keep running in the tray and Start with Windows (2.0), with fakes for the startup entry.</summary>
public sealed class TrayAndStartupTests : IDisposable
{
    private readonly M3Host _m3 = new();

    public void Dispose() => _m3.Dispose();

    [Fact]
    public void AnInstalledCopyRegistersThroughTheEntryItWraps()
    {
        var inner = new M3Host.FakeStartup();
        var startup = new InstalledStartupRegistration(inner, () => true);

        startup.SetEnabled(true);

        Assert.True(startup.IsAvailable);
        Assert.Null(startup.UnavailableReason);
        Assert.True(inner.IsEnabled);
        Assert.True(startup.IsEnabled);
    }

    [Fact]
    public void ACopyThatWasNotInstalledNeverAddsTheEntryButCanRemoveOne()
    {
        var inner = new M3Host.FakeStartup();
        inner.SetEnabled(true); // left by an installed copy earlier
        var startup = new InstalledStartupRegistration(inner, () => false);

        Assert.False(startup.IsAvailable);
        Assert.Equal(InstalledStartupRegistration.NotInstalledReason, startup.UnavailableReason);
        startup.SetEnabled(false);
        Assert.False(inner.IsEnabled);
        Assert.Throws<InvalidOperationException>(() => startup.SetEnabled(true));
        Assert.False(inner.IsEnabled);
    }

    [Fact]
    public void TheInstalledLayoutIsVelopacksCurrentFolderBesideItsUpdater()
    {
        var install = Path.Combine("D:", "Apps", "MementoApp");
        var current = Path.Combine(install, "current") + Path.DirectorySeparatorChar;
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { Path.Combine(install, "Update.exe"), Path.Combine(install, "Memento.exe") };

        Assert.Equal(Path.Combine(install, "Memento.exe"), InstalledLayout.StartupProgram(current, files.Contains));

        files.Remove(Path.Combine(install, "Memento.exe"));
        Assert.Equal(Path.Combine(install, "current", "Memento.exe"), InstalledLayout.StartupProgram(current, files.Contains));

        files.Clear();
        Assert.Null(InstalledLayout.StartupProgram(current, files.Contains));
        Assert.Null(InstalledLayout.StartupProgram(Path.Combine("C:", "src", "Memento", "bin", "Debug"), _ => true));
    }

    [Fact]
    public async Task SettingsSaysWhyStartWithWindowsIsNotAvailableAndRefusesToTurnItOn()
    {
        _m3.Startup.Available = false;

        var general = (await _m3.ResultAsync("settings.get", new { })).GetProperty("general");
        var error = await _m3.ErrorAsync("app.setStartup", new { startWithWindows = true });
        var viaSettings = await _m3.ErrorAsync("settings.set", new { general = new { startWithWindows = true } });

        Assert.False(general.GetProperty("startWithWindowsAvailable").GetBoolean());
        Assert.Equal(InstalledStartupRegistration.NotInstalledReason, general.GetProperty("startWithWindowsNote").GetString());
        Assert.Equal(DomainErrorCodes.AppStartupRefused, error.GetProperty("code").GetString());
        Assert.Equal(
            InstalledStartupRegistration.NotInstalledReason + " Nothing was changed; install Memento with its installer and turn it on there.",
            error.GetProperty("message").GetString());
        Assert.Equal(DomainErrorCodes.AppStartupRefused, viaSettings.GetProperty("code").GetString());
        Assert.False(_m3.Startup.IsEnabled);
        Assert.False(_m3.Host.Settings.Current.General.StartWithWindows);
    }

    [Fact]
    public async Task TheTrayOptionIsOffByDefaultAndTheReadOnlyFieldsMayBeSentBack()
    {
        var before = (await _m3.ResultAsync("settings.get", new { })).GetProperty("general");
        Assert.False(before.GetProperty("keepRunningInTray").GetBoolean());
        Assert.False(before.GetProperty("startWithWindows").GetBoolean());

        // The UI sends the whole block back, read-only fields included; they change nothing.
        var after = (await _m3.ResultAsync("settings.set", new
        {
            general = new { startWithWindows = false, keepRunningInTray = true, language = "en", autoUpdate = true, startWithWindowsAvailable = false, startWithWindowsNote = "x" },
        })).GetProperty("general");

        Assert.True(after.GetProperty("keepRunningInTray").GetBoolean());
        Assert.True(after.GetProperty("startWithWindowsAvailable").GetBoolean());
        Assert.Equal(JsonValueKind.Null, after.GetProperty("startWithWindowsNote").ValueKind);
        Assert.True(_m3.Host.Settings.Current.General.KeepRunningInTray);
    }

    [Fact]
    public void TheTrayOpenScreenEventIsStrictJson()
    {
        Assert.Equal(
            """{"event":"app.openScreen","payload":{"screen":"record"}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.AppOpenScreen, new AppOpenScreenPayload(AppOpenScreenPayload.Record), LiveBridgeJsonContext.Default.BridgeEventEnvelopeAppOpenScreenPayload));
    }

    [Fact]
    public void TheTrayIsQuietWhenNothingRecords()
    {
        var view = TrayView.For(null, CultureInfo.InvariantCulture);

        Assert.Equal(new TrayView("Memento", null, "Open Memento", "New recording", "Quit Memento", false), view);
    }

    [Fact]
    public void TheTrayShowsARecordingInProgressWithItsTimeAndWhatIsSafe()
    {
        var recording = Payload("recording", 754_000);

        var view = TrayView.For(recording, CultureInfo.InvariantCulture);

        Assert.True(view.IsRecording);
        Assert.Equal("Memento · recording 12:34 · 2 audio tracks · saved as it records", view.Tooltip);
        Assert.Equal("Recording 12:34 · started " + recording.StartedAt.ToLocalTime().ToString("t", CultureInfo.InvariantCulture) + " · 2 audio tracks", view.StatusLine);
        Assert.Equal("Show the recording", view.RecordLabel);
        Assert.Equal("Quit Memento (stops the recording; everything so far is kept)", view.QuitLabel);
        Assert.True(view.Tooltip.Length <= TrayView.MaxTooltipLength);
    }

    [Fact]
    public void APausedRecordingSaysWhereItPausedAndThatItIsSaved()
    {
        var view = TrayView.For(Payload("paused", 3_723_000), CultureInfo.InvariantCulture);

        Assert.True(view.IsRecording);
        Assert.Equal("Memento · recording paused at 1:02:03 · everything so far is saved", view.Tooltip);
        Assert.StartsWith("Paused at 1:02:03", view.StatusLine, StringComparison.Ordinal);
    }

    [Fact]
    public void ARecordingBeingSavedIsShownButOffersANewRecording()
    {
        var view = TrayView.For(Payload("finalizing", 60_000), CultureInfo.InvariantCulture);

        Assert.False(view.IsRecording);
        Assert.Equal("Saving the recording · 1:00", view.StatusLine);
        Assert.Equal("New recording", view.RecordLabel);
        Assert.Equal("Quit Memento", view.QuitLabel);
    }

    private static RecordingStatePayload Payload(string state, long elapsedMs) =>
        new(
            "s1",
            "r1",
            state,
            new DateTimeOffset(2026, 10, 10, 10, 0, 0, TimeSpan.FromHours(1)),
            elapsedMs,
            [Track("mic"), Track("system")],
            null,
            0);

    private static Track Track(string id) => new(id, id, "microphone", id, $"tracks/{id}.wav", 48_000, 2, 0, null, 0, null);
}
