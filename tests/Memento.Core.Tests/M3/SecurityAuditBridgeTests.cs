using System.Text.Json.Nodes;
using Memento.Core.Bridge;
using Memento.Core.Projects;

namespace Memento.Core.Tests.M3;

/// <summary>Security audit 2026-10-07: bridge and project-folder findings (SA-06, SA-07, SA-08).</summary>
public sealed class SecurityAuditBridgeTests : IDisposable
{
    private readonly M3Host _m3 = new();

    public void Dispose() => _m3.Dispose();

    [Theory]
    [InlineData("agenda.importFile")]
    [InlineData("attachments.add")]
    [InlineData("library.importMedia")]
    public async Task ThePageCannotNameAFile(string method)
    {
        var id = await _m3.RecordAsync();
        var secret = _m3.WriteFile("secret.txt", "not for the page");
        _m3.Picker.Answer = null;

        var error = await _m3.ErrorAsync(method, new { recordingId = id, path = secret });
        var unc = await _m3.ErrorAsync(method, new { recordingId = id, path = "\\\\203.0.113.9\\share\\agenda.docx" });

        Assert.Equal(BridgeErrorCodes.InvalidParams, error.GetProperty("code").GetString());
        Assert.Equal(BridgeErrorCodes.InvalidParams, unc.GetProperty("code").GetString());
        Assert.Empty(_m3.Picker.Calls);
    }

    [Fact]
    public async Task ACopiedInProjectNamingAFileOutsideItsFolderIsRefused()
    {
        var id = await _m3.RecordAsync();
        var outside = _m3.WriteFile("victim.wav", "RIFF");
        EditManifest(id, m => m["tracks"]![0]!["file"] = "..\\..\\..\\victim.wav");

        var error = await _m3.ErrorAsync("project.get", new { recordingId = id });

        // Reclaim (lossy re-encode, then delete the original) runs as a job: give it time to try.
        await _m3.ResultAsync("storage.reclaim", new { recordingIds = new[] { id }, codec = "mp3", bitrateKbps = 128, downmixMono = false });
        await Task.Delay(TimeSpan.FromSeconds(2));

        Assert.Equal(DomainErrorCodes.ProjectNotFound, error.GetProperty("code").GetString());
        await Assert.ThrowsAsync<ProjectNotFoundException>(() => _m3.Host.Store.LoadAsync(id, CancellationToken.None));
        Assert.True(File.Exists(outside));
        Assert.Equal("RIFF", File.ReadAllText(outside));
    }

    [Fact]
    public async Task AnAttachmentEntryCanOnlyRemoveAFileInTheAttachmentsFolder()
    {
        var id = await _m3.RecordAsync();
        var attachment = (await _m3.ResultPickingAsync("attachments.add", _m3.WriteFile("notes.txt", "n"), new { recordingId = id })).GetProperty("attachment").GetProperty("id").GetString();
        var manifest = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        var track = manifest.Tracks[0].File;
        EditManifest(id, m => m["attachments"]![0]!["file"] = track);

        var error = await _m3.ErrorAsync("attachments.remove", new { recordingId = id, attachmentId = attachment });

        Assert.Equal(DomainErrorCodes.AttachmentsNotFound, error.GetProperty("code").GetString());
        Assert.True(File.Exists(Path.Combine(_m3.Host.Store.GetProjectFolder(id), track.Replace('/', Path.DirectorySeparatorChar))));
    }

    [Theory]
    [InlineData("agenda.docm")]
    [InlineData("help.chm")]
    [InlineData("disk.iso")]
    [InlineData("connect.rdp")]
    [InlineData("query.iqy")]
    [InlineData("note.one")]
    public async Task OnlyDocumentsAndMediaOpenInTheirApp(string name)
    {
        var id = await _m3.RecordAsync();
        var attachment = (await _m3.ResultPickingAsync("attachments.add", _m3.WriteFile(name, "x"), new { recordingId = id })).GetProperty("attachment").GetProperty("id").GetString();

        await _m3.ResultAsync("attachments.open", new { recordingId = id, attachmentId = attachment });

        Assert.Equal(Path.Combine(_m3.Host.Store.GetProjectFolder(id), "attachments"), _m3.Host.Launcher.Opened[^1].LocalPath);
    }

    [Fact]
    public async Task AnAttachmentKeepsItsMarkOfTheWeb()
    {
        var id = await _m3.RecordAsync();
        var source = _m3.WriteFile("from-email.docx", "PK");
        try
        {
            File.WriteAllText(source + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");
        }
        catch (Exception ex) when (ex is IOException or NotSupportedException)
        {
            return; // No alternate data streams on this volume.
        }

        await _m3.ResultPickingAsync("attachments.add", source, new { recordingId = id });

        var copy = Path.Combine(_m3.Host.Store.GetProjectFolder(id), "attachments", "from-email.docx");
        Assert.Contains("ZoneId=3", File.ReadAllText(copy + ":Zone.Identifier"), StringComparison.Ordinal);
    }

    private void EditManifest(string id, Action<JsonObject> edit)
    {
        var path = Path.Combine(_m3.Host.Store.GetProjectFolder(id), ProjectLayout.ManifestFile);
        var node = (JsonObject)JsonNode.Parse(File.ReadAllText(path))!;
        edit(node);
        File.WriteAllText(path, node.ToJsonString());
    }
}
