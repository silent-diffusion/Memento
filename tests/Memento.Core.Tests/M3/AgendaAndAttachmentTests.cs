using System.Text.Json;
using Memento.Core.Agendas;
using Memento.Core.Attachments;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.M3;

/// <summary><c>agenda.*</c>, <c>attachments.*</c> and <c>project.changeType</c> (BRIDGE.md M3).</summary>
public sealed class AgendaAndAttachmentTests : IDisposable
{
    private static readonly string[] DroppedOnly = ["dropped.txt"];
    private static readonly string[] OtherThenDropped = ["other.txt", "dropped.txt"];

    private readonly M3Host _m3 = new();

    public void Dispose() => _m3.Dispose();

    [Fact]
    public async Task PastedTextBecomesAPreviewWithoutAToken()
    {
        var id = await _m3.RecordAsync();

        var result = await _m3.ResultAsync("agenda.parseText", new { recordingId = id, text = "Welcome\nBudget?\nClose" });

        var preview = result.GetProperty("preview");
        Assert.Equal("source,sourceKind,title,items,warnings,ocrEngine,attachmentToken", string.Join(",", preview.EnumerateObject().Select(p => p.Name)));
        Assert.Equal("Pasted text", preview.GetProperty("source").GetString());
        Assert.Equal("pastedText", preview.GetProperty("sourceKind").GetString());
        Assert.Equal(JsonValueKind.Null, preview.GetProperty("attachmentToken").ValueKind);
        Assert.Equal(
            """{"text":"Budget","uncertain":true,"uncertainReason":"It may be two items.","level":0,"location":"line 2"}""",
            preview.GetProperty("items")[1].GetRawText());
        Assert.Equal("""[{"code":"detailsSkipped","message":"Meeting details were left out."}]""", preview.GetProperty("warnings").GetRawText());
    }

    [Fact]
    public async Task ApplyStoresTheItemsAndAttachesTheOriginal()
    {
        var id = await _m3.RecordAsync();
        var file = _m3.WriteFile("agenda.docx", "Welcome\nBudget review\nClose");
        var preview = (await _m3.ResultPickingAsync("agenda.importFile", file, new { recordingId = id })).GetProperty("preview");
        var token = preview.GetProperty("attachmentToken").GetString();
        File.WriteAllText(file, "changed after the import");
        _m3.Sink.Clear();

        var project = await _m3.ResultAsync("agenda.apply", new
        {
            recordingId = id,
            items = new object[] { new { text = " Welcome ", uncertain = false, uncertainReason = (string?)null }, new { text = "Budget review", uncertain = true, uncertainReason = "Merged?" }, new { text = "  ", uncertain = false, uncertainReason = (string?)null } },
            source = "agenda.docx",
            sourceKind = "docx",
            attachmentToken = token,
        });

        var agenda = project.GetProperty("details").GetProperty("agenda");
        Assert.Equal("agenda.docx", agenda.GetProperty("source").GetString());
        Assert.True(agenda.GetProperty("parsedLocally").GetBoolean());
        var items = agenda.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(["Welcome", "Budget review"], items.Select(i => i.GetProperty("text").GetString()));
        Assert.All(items, i => Assert.False(i.GetProperty("covered").GetBoolean()));
        Assert.All(items, i => Assert.StartsWith("a", i.GetProperty("id").GetString(), StringComparison.Ordinal));
        Assert.Equal("Merged?", items[1].GetProperty("uncertainReason").GetString());

        var attachment = (await _m3.ResultAsync("attachments.list", new { recordingId = id })).GetProperty("attachments").EnumerateArray().Single();
        Assert.Equal("agenda", attachment.GetProperty("kind").GetString());
        Assert.Equal("agenda.docx", attachment.GetProperty("name").GetString());
        var copy = Path.Combine(_m3.Host.Store.GetProjectFolder(id), "attachments", "agenda.docx");
        Assert.Equal("Welcome\nBudget review\nClose", File.ReadAllText(copy));
        Assert.Equal(0, _m3.Get<PendingAgendaFiles>().Count);

        var history = project.GetProperty("history").EnumerateArray().Select(h => h.GetProperty("summary").GetString()).ToList();
        Assert.Contains("Agenda imported", history);
        Assert.Contains("Agenda file attached", history);
        Assert.NotEmpty(_m3.Sink.Payloads(BridgeEventNames.LibraryChanged));
    }

    [Fact]
    public async Task ApplyRefusesTooManyOrTooLongItems()
    {
        var id = await _m3.RecordAsync();
        var many = Enumerable.Range(1, 201).Select(i => new { text = $"Item {i}", uncertain = false }).ToArray();
        var longItem = new[] { new { text = "Fine", uncertain = false }, new { text = new string('x', 201), uncertain = false } };

        var tooMany = await _m3.ErrorAsync("agenda.apply", new { recordingId = id, items = many, source = "Pasted text", sourceKind = "pastedText" });
        var tooLong = await _m3.ErrorAsync("agenda.apply", new { recordingId = id, items = longItem, source = "Pasted text", sourceKind = "pastedText" });
        var badKind = await _m3.ErrorAsync("agenda.apply", new { recordingId = id, items = longItem[..1], source = "x", sourceKind = "pptx" });

        Assert.Equal(DomainErrorCodes.AgendaTooManyItems, tooMany.GetProperty("code").GetString());
        Assert.Contains("201 items", tooMany.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(DomainErrorCodes.AgendaItemTooLong, tooLong.GetProperty("code").GetString());
        Assert.Equal("2", tooLong.GetProperty("detail").GetString());
        Assert.Contains("Item 2", tooLong.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(BridgeErrorCodes.InvalidParams, badKind.GetProperty("code").GetString());
        Assert.Empty((await _m3.Host.Store.LoadAsync(id, CancellationToken.None)).Details.Agenda.Items);

        // Exactly 200 items of 200 characters are fine.
        var limit = Enumerable.Range(1, 200).Select(i => new { text = i.ToString("D3", System.Globalization.CultureInfo.InvariantCulture) + new string('y', 197), uncertain = false }).ToArray();
        await _m3.ResultAsync("agenda.apply", new { recordingId = id, items = limit, source = "Pasted text", sourceKind = "pastedText" });
        Assert.Equal(200, (await _m3.Host.Store.LoadAsync(id, CancellationToken.None)).Details.Agenda.Items.Count);
    }

    [Fact]
    public async Task ImportsNameTheirErrors()
    {
        var id = await _m3.RecordAsync();
        var unreadable = _m3.WriteFile("broken.txt", "ERR");

        var error = await _m3.ErrorPickingAsync("agenda.importFile", unreadable, new { recordingId = id });
        var missing = await _m3.ErrorPickingAsync("agenda.importFile", _m3.Directory.File("gone.docx"), new { recordingId = id });
        var relative = await _m3.ErrorPickingAsync("agenda.importFile", "agenda.docx", new { recordingId = id });
        var noProject = await _m3.ErrorAsync("agenda.parseText", new { recordingId = "20260101-000000-aaaaaa", text = "x" });

        Assert.Equal(DomainErrorCodes.AgendaUnreadable, error.GetProperty("code").GetString());
        Assert.Equal(BridgeErrorCodes.InvalidParams, missing.GetProperty("code").GetString());
        Assert.Contains("gone.docx", missing.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(BridgeErrorCodes.InvalidParams, relative.GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.ProjectNotFound, noProject.GetProperty("code").GetString());
        Assert.Equal(0, _m3.Get<PendingAgendaFiles>().Count);
    }

    [Fact]
    public async Task ThePickerIsShownWithTheAgendaFilters()
    {
        var id = await _m3.RecordAsync();

        var cancelled = await _m3.ResultAsync("agenda.importFile", new { recordingId = id });
        _m3.Picker.Answer = _m3.WriteFile("plan.txt", "One\nTwo");
        var chosen = await _m3.ResultAsync("agenda.importFile", new { recordingId = id });

        Assert.Equal("""{"preview":null,"cancelled":true}""", cancelled.GetRawText());
        Assert.False(chosen.GetProperty("cancelled").GetBoolean());
        Assert.Equal("plan.txt", chosen.GetProperty("preview").GetProperty("source").GetString());
        var filters = _m3.Picker.Calls[0].Filters;
        Assert.Contains(filters, f => f.Patterns.Contains("*.docx"));
        Assert.Contains(filters, f => f.Patterns.Contains("*.png"));
        Assert.Equal("*.*", filters[^1].Patterns.Single());
    }

    [Fact]
    public async Task ADroppedFileResolvesByNameOrSaysToUseThePicker()
    {
        var id = await _m3.RecordAsync();
        var file = _m3.WriteFile("dropped.txt", "Alpha\nBeta");

        var unavailable = await _m3.ErrorAsync("agenda.importDropped", new { recordingId = id, paths = DroppedOnly });
        _m3.Get<DroppedFiles>().Register([file]);
        var resolved = await _m3.ResultAsync("agenda.importDropped", new { recordingId = id, paths = OtherThenDropped });

        Assert.Equal(DomainErrorCodes.AgendaDropUnavailable, unavailable.GetProperty("code").GetString());
        Assert.Equal("dropped.txt", resolved.GetProperty("preview").GetProperty("source").GetString());
        Assert.Equal(2, resolved.GetProperty("preview").GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task APreviewCanBeMadeBeforeTheRecordingExists()
    {
        var preview = (await _m3.ResultPickingAsync("agenda.importFile", _m3.WriteFile("early.txt", "One\nTwo"), new { recordingId = (string?)null })).GetProperty("preview");
        var pasted = (await _m3.ResultAsync("agenda.parseText", new { recordingId = (string?)null, text = "Alpha\nBeta" })).GetProperty("preview");
        var id = await _m3.RecordAsync();
        var items = preview.GetProperty("items").EnumerateArray().Select(i => new { text = i.GetProperty("text").GetString(), uncertain = false }).ToArray();

        var noId = await _m3.ErrorAsync("agenda.apply", new { recordingId = (string?)null, items, source = "early.txt", sourceKind = "text" });
        var project = await _m3.ResultAsync("agenda.apply", new { recordingId = id, items, source = "early.txt", sourceKind = "text", attachmentToken = preview.GetProperty("attachmentToken").GetString() });

        Assert.Equal(2, pasted.GetProperty("items").GetArrayLength());
        Assert.Equal(DomainErrorCodes.ProjectNotFound, noId.GetProperty("code").GetString());
        Assert.Equal(2, project.GetProperty("details").GetProperty("agenda").GetProperty("items").GetArrayLength());
        var attachment = (await _m3.ResultAsync("attachments.list", new { recordingId = id })).GetProperty("attachments").EnumerateArray().Single();
        Assert.Equal("early.txt", attachment.GetProperty("name").GetString());
        Assert.Equal("agenda", attachment.GetProperty("kind").GetString());
        Assert.True(PendingAgendaOptions.DefaultExpiry >= TimeSpan.FromMinutes(30));
    }

    [Fact]
    public async Task DiscardForgetsTheHeldFile()
    {
        var id = await _m3.RecordAsync();
        var preview = (await _m3.ResultPickingAsync("agenda.importFile", _m3.WriteFile("a.txt", "One"), new { recordingId = id })).GetProperty("preview");
        Assert.Equal(1, _m3.Get<PendingAgendaFiles>().Count);

        Assert.Equal("{}", (await _m3.ResultAsync("agenda.discard", new { attachmentToken = preview.GetProperty("attachmentToken").GetString() })).GetRawText());
        Assert.Equal("{}", (await _m3.ResultAsync("agenda.discard", new { attachmentToken = "unknown" })).GetRawText());
        Assert.Equal(0, _m3.Get<PendingAgendaFiles>().Count);
        Assert.Empty(Directory.GetFiles(_m3.Directory.File("pending")));
    }

    [Fact]
    public async Task HeldFilesExpire()
    {
        var time = new ManualTimeProvider(new DateTimeOffset(2026, 10, 6, 9, 0, 0, TimeSpan.Zero));
        using var pending = new PendingAgendaFiles(new PendingAgendaOptions(_m3.Directory.File("held"), TimeSpan.FromHours(1)), time);
        var token = await pending.HoldAsync(_m3.WriteFile("x.txt", "x"), "r1", CancellationToken.None);

        time.Advance(TimeSpan.FromMinutes(59));
        Assert.NotNull(pending.Find(token));
        time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(pending.Find(token));
        Assert.Empty(Directory.GetFiles(_m3.Directory.File("held")));
    }

    [Fact]
    public async Task AnExpiredTokenStillAppliesTheItems()
    {
        var id = await _m3.RecordAsync();

        var project = await _m3.ResultAsync("agenda.apply", new { recordingId = id, items = new[] { new { text = "Kept", uncertain = false } }, source = "a.docx", sourceKind = "docx", attachmentToken = "expired" });

        Assert.Equal("Kept", project.GetProperty("details").GetProperty("agenda").GetProperty("items")[0].GetProperty("text").GetString());
        Assert.Contains(project.GetProperty("history").EnumerateArray(), h => h.GetProperty("detail").GetString()?.Contains("not attached", StringComparison.Ordinal) == true);
        Assert.Empty((await _m3.ResultAsync("attachments.list", new { recordingId = id })).GetProperty("attachments").EnumerateArray());
    }

    [Fact]
    public async Task SetCoveredWorksDuringRecording()
    {
        var (sessionId, id) = await _m3.Host.StartAsync("Live", TestRecordings.Mic);
        var project = await _m3.ResultAsync("agenda.apply", new { recordingId = id, items = new[] { new { text = "One", uncertain = false }, new { text = "Two", uncertain = false } }, source = "Pasted text", sourceKind = "pastedText" });
        var second = project.GetProperty("details").GetProperty("agenda").GetProperty("items")[1].GetProperty("id").GetString();

        var result = await _m3.ResultAsync("agenda.setCovered", new { recordingId = id, itemId = second, covered = true });
        var missing = await _m3.ErrorAsync("agenda.setCovered", new { recordingId = id, itemId = "a-missing", covered = true });

        Assert.Equal([false, true], result.GetProperty("agenda").GetProperty("items").EnumerateArray().Select(i => i.GetProperty("covered").GetBoolean()));
        Assert.Equal(DomainErrorCodes.AgendaItemNotFound, missing.GetProperty("code").GetString());
        Assert.Equal("a-missing", missing.GetProperty("detail").GetString());
        await _m3.Host.ResultAsync("recording.stop", JsonSerializer.Serialize(new { sessionId }));
        await _m3.Host.Recordings.WhenIdleAsync();
        Assert.True((await _m3.Host.Store.LoadAsync(id, CancellationToken.None)).Details.Agenda.Items[1].Covered);
    }

    [Fact]
    public async Task AttachmentsAreCopiedIndexedAndRemoved()
    {
        var id = await _m3.RecordAsync();
        var before = (await _m3.ResultAsync("project.get", new { recordingId = id })).GetProperty("sizeBytes").GetInt64();
        var source = _m3.WriteFile("Slides: final?.pdf", new string('p', 5000));

        var first = (await _m3.ResultPickingAsync("attachments.add", source, new { recordingId = id })).GetProperty("attachment");
        var second = (await _m3.ResultPickingAsync("attachments.add", source, new { recordingId = id })).GetProperty("attachment");

        Assert.Equal("id,name,sizeBytes,addedAt,kind,contentType", string.Join(",", first.EnumerateObject().Select(p => p.Name)));
        Assert.Equal("Slides final.pdf", first.GetProperty("name").GetString());
        Assert.Equal("Slides final (2).pdf", second.GetProperty("name").GetString());
        Assert.Equal(5000, first.GetProperty("sizeBytes").GetInt64());
        Assert.Equal("file", first.GetProperty("kind").GetString());
        Assert.Equal("application/pdf", first.GetProperty("contentType").GetString());

        var manifest = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        var records = manifest.Attachments;
        Assert.Equal(["attachments/Slides final.pdf", "attachments/Slides final (2).pdf"], records.Select(r => r.File));
        Assert.Equal(await FileHashes.Sha256Async(source, CancellationToken.None), records[0].Sha256);

        var project = await _m3.ResultAsync("project.get", new { recordingId = id });
        Assert.InRange(project.GetProperty("sizeBytes").GetInt64() - before, 10_000, 20_000);
        var listed = await _m3.Host.Index.QueryAsync(Memento.Core.Library.LibraryQuery.All, CancellationToken.None);
        Assert.Equal(project.GetProperty("sizeBytes").GetInt64(), listed.Recordings.Single().SizeBytes);

        await _m3.ResultAsync("attachments.remove", new { recordingId = id, attachmentId = first.GetProperty("id").GetString() });
        var left = (await _m3.ResultAsync("attachments.list", new { recordingId = id })).GetProperty("attachments");
        Assert.Equal("Slides final (2).pdf", left.EnumerateArray().Single().GetProperty("name").GetString());
        Assert.False(File.Exists(Path.Combine(_m3.Host.Store.GetProjectFolder(id), "attachments", "Slides final.pdf")));
        var history = (await _m3.ResultAsync("project.get", new { recordingId = id })).GetProperty("history").EnumerateArray().Select(h => h.GetProperty("summary").GetString()).ToList();
        Assert.Equal(2, history.Count(s => s == "Attachment added"));
        Assert.Contains("Attachment removed", history);

        var missing = await _m3.ErrorAsync("attachments.remove", new { recordingId = id, attachmentId = first.GetProperty("id").GetString() });
        Assert.Equal(DomainErrorCodes.AttachmentsNotFound, missing.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AttachmentsOver100MegabytesAreRefused()
    {
        var id = await _m3.RecordAsync();
        var big = _m3.Directory.File("big.zip");
        using (var stream = File.Create(big))
        {
            stream.SetLength(AttachmentService.MaxBytes + 1);
        }

        var error = await _m3.ErrorPickingAsync("attachments.add", big, new { recordingId = id });

        Assert.Equal(DomainErrorCodes.AttachmentsTooLarge, error.GetProperty("code").GetString());
        Assert.Contains("100 MB", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(Directory.GetFiles(Path.Combine(_m3.Host.Store.GetProjectFolder(id), "attachments")));
    }

    [Fact]
    public async Task OpenUsesTheDefaultAppButNeverRunsPrograms()
    {
        var id = await _m3.RecordAsync();
        var doc = (await _m3.ResultPickingAsync("attachments.add", _m3.WriteFile("notes.txt", "n"), new { recordingId = id })).GetProperty("attachment").GetProperty("id").GetString();
        var exe = (await _m3.ResultPickingAsync("attachments.add", _m3.WriteFile("setup.exe", "MZ"), new { recordingId = id })).GetProperty("attachment").GetProperty("id").GetString();
        _m3.Picker.Answer = null;

        await _m3.ResultAsync("attachments.open", new { recordingId = id, attachmentId = doc });
        await _m3.ResultAsync("attachments.open", new { recordingId = id, attachmentId = exe });
        var cancelled = await _m3.ResultAsync("attachments.add", new { recordingId = id });

        var folder = Path.Combine(_m3.Host.Store.GetProjectFolder(id), "attachments");
        Assert.Equal(Path.Combine(folder, "notes.txt"), _m3.Host.Launcher.Opened[0].LocalPath);
        Assert.Equal(folder, _m3.Host.Launcher.Opened[1].LocalPath);
        Assert.Equal("""{"attachment":null,"cancelled":true}""", cancelled.GetRawText());
    }

    [Theory]
    [InlineData("help.chm")]
    [InlineData("installer.msix")]
    [InlineData("disk.iso")]
    [InlineData("tool.py")]
    [InlineData("macros.xlsm")]
    [InlineData("search.search-ms")]
    public async Task OnlyDocumentsImagesAndMediaOpenAnythingElseShowsItsFolder(string name)
    {
        var id = await _m3.RecordAsync();
        var attachment = (await _m3.ResultPickingAsync("attachments.add", _m3.WriteFile(name, "x"), new { recordingId = id })).GetProperty("attachment").GetProperty("id").GetString();

        await _m3.ResultAsync("attachments.open", new { recordingId = id, attachmentId = attachment });

        Assert.Equal(Path.Combine(_m3.Host.Store.GetProjectFolder(id), "attachments"), _m3.Host.Launcher.Opened.Single().LocalPath);
    }

    [Fact]
    public async Task ADownloadedFileKeepsItsInternetMarkWhenAttached()
    {
        var id = await _m3.RecordAsync();
        var source = _m3.WriteFile("downloaded.docx", "PK");
        File.WriteAllText(source + ":Zone.Identifier", "[ZoneTransfer]\r\nZoneId=3\r\n");

        await _m3.ResultPickingAsync("attachments.add", source, new { recordingId = id });

        var copy = Path.Combine(_m3.Host.Store.GetProjectFolder(id), "attachments", "downloaded.docx");
        Assert.Equal("[ZoneTransfer]\r\nZoneId=3\r\n", File.ReadAllText(copy + ":Zone.Identifier"));
    }

    [Fact]
    public async Task ChangeTypeAcceptsCustomTypesUpTo40Characters()
    {
        var id = await _m3.RecordAsync();

        var project = await _m3.ResultAsync("project.changeType", new { recordingId = id, type = "  Board review  " });
        var tooLong = await _m3.ErrorAsync("project.changeType", new { recordingId = id, type = new string('t', 41) });
        var blank = await _m3.ErrorAsync("project.changeType", new { recordingId = id, type = " " });
        var unknown = await _m3.ErrorAsync("project.changeType", new { recordingId = "20260101-000000-aaaaaa", type = "meeting" });

        Assert.Equal("Board review", project.GetProperty("summary").GetProperty("type").GetString());
        Assert.Equal("Board review", project.GetProperty("details").GetProperty("type").GetString());
        Assert.Contains(project.GetProperty("history").EnumerateArray(), h => h.GetProperty("summary").GetString() == "Type changed" && h.GetProperty("detail").GetString() == "meeting → Board review");
        Assert.Equal(BridgeErrorCodes.InvalidParams, tooLong.GetProperty("code").GetString());
        Assert.Equal(BridgeErrorCodes.InvalidParams, blank.GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.ProjectNotFound, unknown.GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("Quarterly: plan / review?", "Quarterly plan review")]
    [InlineData("CON", "_CON")]
    [InlineData("  ...  ", "Recording")]
    [InlineData("a\u0007b", "a b")]
    public void FileNamesAreSafeOnWindows(string title, string expected)
    {
        Assert.Equal(expected, FileNames.Sanitize(title, "Recording"));
    }

    [Fact]
    public void UniqueNamesNeverReplaceAFile()
    {
        File.WriteAllText(_m3.Directory.File("a.txt"), "x");
        var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "a (2).txt" };

        Assert.Equal("a (3).txt", FileNames.Unique(_m3.Directory.Path, "a.txt", taken));
        Assert.Equal("b.txt", FileNames.Unique(_m3.Directory.Path, "b.txt", taken));
        Assert.Contains("a (3).txt", taken);
    }
}
