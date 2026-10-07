using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.M3;

/// <summary>Pins the JSON of the M3 events and of the requests the UI sends (BRIDGE.md M3).</summary>
public sealed class M3ContractSerializationTests
{
    [Fact]
    public void ExportProgressEvent()
    {
        Assert.Equal(
            """{"event":"export.progress","payload":{"jobId":"x1","recordingId":"r1","percent":42,"currentFile":"a.flac","state":"running","message":null,"outputFolder":"D:\\Exports\\a","files":2,"bytes":1024}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.ExportProgress, new ExportProgressPayload("x1", "r1", 42, "a.flac", "running", null, @"D:\Exports\a", 2, 1024), M3BridgeJsonContext.Default.BridgeEventEnvelopeExportProgressPayload));
    }

    [Fact]
    public void LibraryMoveAndReclaimProgressEvents()
    {
        Assert.Equal(
            """{"event":"library.moveProgress","payload":{"jobId":"m1","percent":50,"state":"running","message":"Copied and checked 3 files","newPath":"D:\\Library"}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.LibraryMoveProgress, new LibraryMoveProgressPayload("m1", 50, "running", "Copied and checked 3 files", @"D:\Library"), M3BridgeJsonContext.Default.BridgeEventEnvelopeLibraryMoveProgressPayload));
        Assert.Equal(
            """{"event":"storage.reclaimProgress","payload":{"jobId":"r1","percent":100,"state":"done","message":null,"recordingsDone":2,"bytesFreed":2048}}""",
            BridgeEventPublisher.Serialize(BridgeEventNames.StorageReclaimProgress, new StorageReclaimProgressPayload("r1", 100, "done", null, 2, 2048), M3BridgeJsonContext.Default.BridgeEventEnvelopeStorageReclaimProgressPayload));
    }

    [Fact]
    public async Task UnknownFieldsAndMissingRequiredFieldsAreInvalidParams()
    {
        using var host = new BridgeTestHost();

        var extra = await host.CallAsync("agenda.parseText", """{"recordingId":"x","text":"a","language":"en"}""");
        var missing = await host.CallAsync("export.run", """{"recordingId":"x","selection":{}}""");
        var wrongType = await host.CallAsync("app.setStartup", """{"startWithWindows":"yes"}""");

        Assert.All(new[] { extra, missing, wrongType }, r => Assert.Equal(BridgeErrorCodes.InvalidParams, r.GetProperty("error").GetProperty("code").GetString()));
    }

    [Fact]
    public async Task UnknownRecordingsAreProjectNotFound()
    {
        using var host = new BridgeTestHost();
        const string id = "\"recordingId\":\"20260101-000000-aaaaaa\"";

        foreach (var (method, parameters) in new[]
        {
            ("agenda.importFile", "{" + id + "}"),
            ("agenda.apply", "{" + id + ",\"items\":[],\"source\":\"a\",\"sourceKind\":\"text\"}"),
            ("agenda.setCovered", "{" + id + ",\"itemId\":\"a1\",\"covered\":true}"),
            ("attachments.list", "{" + id + "}"),
            ("attachments.add", "{" + id + "}"),
            ("attachments.open", "{" + id + ",\"attachmentId\":\"f1\"}"),
            ("project.changeType", "{" + id + ",\"type\":\"meeting\"}"),
        })
        {
            var response = await host.CallAsync(method, parameters);
            Assert.True(response.TryGetProperty("error", out var error), method);
            Assert.Equal(DomainErrorCodes.ProjectNotFound, error.GetProperty("code").GetString());
        }
    }
}
