using System.Text.Json;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Status;
using Memento.Core.Tests.Fakes;
using Memento.Core.Updates;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Core.Tests.Status;

/// <summary>Self-update through the real service and bridge with a fake feed (H1).</summary>
public sealed class UpdateServiceTests : IDisposable
{
    private readonly FakeUpdateClient _client;
    private readonly BridgeTestHost _host;
    private readonly CancellationTokenSource _stop = new();

    public UpdateServiceTests()
        : this(new FakeUpdateClient())
    {
    }

    private UpdateServiceTests(FakeUpdateClient client)
    {
        _client = client;
        _host = new BridgeTestHost(configure: services => services.AddSingleton<IUpdateClient>(client));
        Service.IdlePoll = TimeSpan.FromMilliseconds(20);
    }

    private UpdateService Service => _host.Get<UpdateService>();

    public void Dispose()
    {
        _stop.Cancel();
        _stop.Dispose();
        _host.Dispose();
    }

    private Task RunScheduleAsync(Task? uiReady = null) => Task.Run(() => Service.RunAsync(uiReady ?? Task.CompletedTask, _stop.Token));

    private Task<JsonElement> StateAsync(string state, int timeoutMs = 10_000) =>
        _host.Sink.WaitForAsync(BridgeEventNames.UpdatesProgress, p => p.GetProperty("state").GetString() == state, timeoutMs);

    private void SetRecording(bool active) => _host.Get<RecordingStatusBoard>().SetRecording(new RecordingFooterStatus(active, null, null));

    [Fact]
    public async Task OnceTheInterfaceIsReadyANewerVersionIsFoundDownloadedAndOfferedForRestart()
    {
        _client.Available = "0.5.1";
        var uiReady = new TaskCompletionSource();
        var schedule = RunScheduleAsync(uiReady.Task);

        await Task.Delay(100);
        Assert.Equal(0, _client.Checks); // nothing before the interface is ready
        uiReady.SetResult();

        var ready = await StateAsync(UpdateStates.Ready);
        Assert.Equal("0.5.1", ready.GetProperty("availableVersion").GetString());
        Assert.Equal(100, ready.GetProperty("percent").GetInt32());
        Assert.Equal(JsonValueKind.Null, ready.GetProperty("message").ValueKind); // automatic: no words, only the toast
        Assert.Equal(1, _client.Checks);
        Assert.Equal(1, _client.Downloads);
        var states = _host.Sink.Payloads(BridgeEventNames.UpdatesProgress).Select(p => p.GetProperty("state").GetString()).Distinct().ToList();
        Assert.Equal([UpdateStates.Checking, UpdateStates.Idle, UpdateStates.Downloading, UpdateStates.Ready], states);
        Assert.Contains(_host.Sink.Payloads(BridgeEventNames.UpdatesProgress), p => p.GetProperty("state").GetString() == UpdateStates.Downloading && p.GetProperty("percent").GetInt32() == 42);
        Assert.Equal(UpdateFooterStatus.Idle, _host.Get<UpdateStatusBoard>().Current);
        Assert.Equal(0, _client.Applies); // installing waits for a click or the next start

        _stop.Cancel();
        await schedule;
    }

    [Fact]
    public async Task TheFooterShowsTheDownloadWhileItRuns()
    {
        _client.Available = "0.5.1";
        _client.DownloadGate = new TaskCompletionSource();
        var schedule = RunScheduleAsync();

        await _host.Sink.WaitForAsync(BridgeEventNames.UpdatesProgress, p => p.GetProperty("percent").ValueKind == JsonValueKind.Number && p.GetProperty("percent").GetInt32() == 42);
        Assert.Equal(new UpdateFooterStatus(true, 42, "0.5.1"), _host.Get<UpdateStatusBoard>().Current);
        var footer = _host.Get<FooterStatusService>().Compute();
        Assert.Equal(new UpdateFooterStatus(true, 42, "0.5.1"), footer.Update);

        _client.DownloadGate.SetResult();
        await StateAsync(UpdateStates.Ready);
        Assert.False(_host.Get<UpdateStatusBoard>().Current.Downloading);
        _stop.Cancel();
        await schedule;
    }

    [Fact]
    public async Task WithAutomaticUpdatesOffNothingIsCheckedButCheckNowStillWorks()
    {
        await _host.ResultAsync("settings.set", """{"general":{"autoUpdate":false}}""");
        _client.Available = "0.5.1";
        var schedule = RunScheduleAsync();
        await Task.Delay(200);

        Assert.Equal(0, _client.Checks);
        Assert.False((await _host.ResultAsync("settings.get")).GetProperty("general").GetProperty("autoUpdate").GetBoolean());

        var checkedNow = await _host.ResultAsync(BridgeMethodNames.UpdatesCheck);
        Assert.Equal(1, _client.Checks);
        Assert.Equal("0.5.1", checkedNow.GetProperty("availableVersion").GetString());
        await StateAsync(UpdateStates.Ready);
        _stop.Cancel();
        await schedule;
    }

    [Fact]
    public async Task NothingIsCheckedWhileARecordingRunsAndTheCheckFollowsWhenItEnds()
    {
        _client.Available = "0.5.1";
        SetRecording(true);
        var schedule = RunScheduleAsync();
        await Task.Delay(200);
        Assert.Equal(0, _client.Checks);

        SetRecording(false);
        await StateAsync(UpdateStates.Ready);
        Assert.Equal(1, _client.Checks);
        _stop.Cancel();
        await schedule;
    }

    [Fact]
    public async Task ARecordingThatStartsMidDownloadStopsItAndItRunsAgainAfterwards()
    {
        _client.Available = "0.5.1";
        _client.DownloadGate = new TaskCompletionSource();
        var checkedNow = await _host.ResultAsync(BridgeMethodNames.UpdatesCheck);
        Assert.Equal("0.5.1", checkedNow.GetProperty("availableVersion").GetString());
        await StateAsync(UpdateStates.Downloading);

        SetRecording(true);
        await _host.Sink.WaitForAsync(BridgeEventNames.UpdatesProgress, p => p.GetProperty("deferred").GetBoolean());
        var deferred = Service.Status;
        Assert.Equal(UpdateStates.Idle, deferred.State);
        Assert.Equal("Memento 0.5.1 is available. It downloads once the recording and processing have finished.", deferred.Message);
        Assert.False(_host.Get<UpdateStatusBoard>().Current.Downloading);

        _client.DownloadGate = null;
        SetRecording(false);
        await StateAsync(UpdateStates.Ready);
        Assert.Equal(2, _client.Downloads);
    }

    [Fact]
    public async Task AFailedCheckByHandSaysWhatHappenedAndAnAutomaticOneOnlyLogs()
    {
        _client.CheckFailure = new UpdateCheckException("the update server could not be reached");
        var schedule = RunScheduleAsync();
        await _host.Sink.WaitForAsync(BridgeEventNames.UpdatesProgress, p => p.GetProperty("state").GetString() == UpdateStates.Idle && _client.Checks == 1);
        Assert.Null(Service.Status.Message);
        _stop.Cancel();
        await schedule;

        var manual = await _host.ResultAsync(BridgeMethodNames.UpdatesCheck);

        Assert.Equal(UpdateStates.Failed, manual.GetProperty("state").GetString());
        Assert.Equal(
            "Memento could not check for updates: the update server could not be reached. Nothing was downloaded and this version keeps working as it is. Check the internet connection, then choose Check now again.",
            manual.GetProperty("message").GetString());
    }

    [Fact]
    public async Task CheckNowSaysWhenThisIsTheNewestVersion()
    {
        var result = await _host.ResultAsync(BridgeMethodNames.UpdatesCheck);

        Assert.Equal(UpdateStates.Idle, result.GetProperty("state").GetString());
        Assert.Equal("Memento 0.5.0 is the newest version.", result.GetProperty("message").GetString());
        Assert.Equal(JsonValueKind.String, result.GetProperty("lastCheckedAt").ValueKind);
        Assert.Equal(0, _client.Downloads);
    }

    [Fact]
    public async Task AFailedDownloadByHandKeepsThisVersionAndSaysSo()
    {
        _client.Available = "0.5.1";
        _client.DownloadFailure = new UpdateCheckException("the update could not be saved (disk full)");

        await _host.ResultAsync(BridgeMethodNames.UpdatesCheck);
        var failed = await StateAsync(UpdateStates.Failed);

        Assert.Equal(
            "Memento 0.5.1 could not be downloaded: the update could not be saved (disk full). Nothing was changed and this version keeps working as it is. Choose Check now to try again.",
            failed.GetProperty("message").GetString());
    }

    [Fact]
    public async Task RestartToUpdateNeedsADownloadAndNeverInterruptsARecording()
    {
        var notReady = await _host.CallAsync(BridgeMethodNames.UpdatesApply);
        Assert.Equal(DomainErrorCodes.UpdatesNotReady, notReady.GetProperty("error").GetProperty("code").GetString());

        _client.Available = "0.5.1";
        await _host.ResultAsync(BridgeMethodNames.UpdatesCheck);
        await StateAsync(UpdateStates.Ready);
        SetRecording(true);
        var busy = await _host.CallAsync(BridgeMethodNames.UpdatesApply);
        Assert.Equal(DomainErrorCodes.UpdatesBusy, busy.GetProperty("error").GetProperty("code").GetString());
        Assert.Contains("Stop the recording first", busy.GetProperty("error").GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(0, _client.Applies);

        SetRecording(false);
        await _host.ResultAsync(BridgeMethodNames.UpdatesApply);
        Assert.Equal(1, _client.Applies);
    }

    [Fact]
    public async Task AnUpdateDownloadedInAnEarlierRunIsReadyAtOnce()
    {
        using var host = new BridgeTestHost(configure: s => s.AddSingleton<IUpdateClient>(new FakeUpdateClient { PendingVersion = "0.5.1" }));

        var status = await host.ResultAsync(BridgeMethodNames.UpdatesStatus);

        Assert.Equal(UpdateStates.Ready, status.GetProperty("state").GetString());
        Assert.Equal("0.5.1", status.GetProperty("availableVersion").GetString());
    }

    [Fact]
    public async Task ACopyThatCannotUpdateItselfNeverChecksAndSaysWhy()
    {
        using var host = new BridgeTestHost(); // the default client: a build folder, not installed with Setup

        var status = await host.ResultAsync(BridgeMethodNames.UpdatesStatus);
        var check = await host.CallAsync(BridgeMethodNames.UpdatesCheck);

        Assert.Equal(UpdateStates.Unavailable, status.GetProperty("state").GetString());
        Assert.Equal(DomainErrorCodes.UpdatesUnavailable, check.GetProperty("error").GetProperty("code").GetString());
        await host.Get<UpdateService>().RunAsync(Task.CompletedTask, CancellationToken.None); // returns at once
    }

    [Theory]
    [InlineData("0.2.0", true)]
    [InlineData("0.4.0", true)]
    [InlineData("0.4.9", true)]
    [InlineData("0.5.0", false)]
    [InlineData("0.5.1", false)]
    [InlineData("1.0.0", false)]
    [InlineData("0.6.0-rc.1", true)]
    [InlineData("0.5.1-rc", true)]
    [InlineData("1.0.0+abc", false)]
    [InlineData("garbage", false)]
    public void PreReleasesAreOfferedOnlyToPreReleaseBuilds(string version, bool accepts) =>
        Assert.Equal(accepts, UpdatePolicy.AcceptsPreReleases(version));
}
