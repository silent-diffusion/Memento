using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.M3;

/// <summary>
/// Security audit 2026-10-07, item 5: with AI off, a whole session (record from the simulated engine, finalize,
/// transcription with no model installed, review, agenda, attachments, export, settings, models list) makes no
/// network request and opens no socket from managed code.
/// </summary>
[Collection(nameof(NetworkSilenceGroup))]
public sealed class NetworkSilenceTests : IDisposable
{
    private readonly M3Host _m3 = new();

    public void Dispose() => _m3.Dispose();

    /// <summary>The control: the spy does see a request and a connect when one happens.</summary>
    [Fact]
    public async Task TheSpySeesARequest()
    {
        var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
            using var spy = new NetworkSpy();
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var accept = listener.AcceptTcpClientAsync();
            var request = http.GetAsync(new Uri($"http://127.0.0.1:{port}/probe"));
            using (await accept)
            {
            }

            await Assert.ThrowsAnyAsync<Exception>(() => request);
            Assert.Contains(spy.Events, e => e.StartsWith("http GET http://127.0.0.1:", StringComparison.Ordinal));
            Assert.Contains(spy.Events, e => e.StartsWith("connect ", StringComparison.Ordinal));
        }
        finally
        {
            listener.Stop();
        }
    }

    [Fact]
    public async Task AFullSessionWithAiOffTouchesNoNetwork()
    {
        using var spy = new NetworkSpy();

        var id = await _m3.RecordAsync("Network check", seconds: 3);
        _m3.WriteTranscript(id);
        await _m3.ResultAsync("project.get", new { recordingId = id });
        await _m3.ResultAsync("transcript.get", new { recordingId = id });
        await _m3.ResultAsync("library.list", new { });
        await _m3.ResultAsync("agenda.parseText", new { recordingId = id, text = "Welcome\nBudget\nClose" });
        await _m3.ResultPickingAsync("attachments.add", _m3.WriteFile("agenda.txt", "1. Welcome"), new { recordingId = id });
        await _m3.ResultAsync("settings.get", new { });
        await _m3.ResultAsync("models.list", new { });
        await _m3.ResultAsync("status.get", new { });
        var selection = new ExportSelection { Transcript = new ExportTranscriptChoice { On = true, Formats = ["json", "markdown", "text", "srt"] } };
        await _m3.ResultAsync("export.run", new { recordingId = id, selection, destination = new { folder = _m3.Directory.File("Exports"), createSubfolder = true }, remember = false });
        await _m3.Get<ExportService>().WhenIdleAsync();

        Assert.Empty(spy.Events);
        Assert.False((await _m3.Host.Settings.LoadAsync(CancellationToken.None)).Ai.Enabled);
    }
}
