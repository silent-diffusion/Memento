using System.Text.Json;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Export;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.M3;

/// <summary><c>export.*</c> (BRIDGE.md M3) with copy-only encoders: what is written, named, hashed and cleaned up.</summary>
public sealed class ExportTests : IDisposable
{
    private static readonly string[] AllTranscriptFormats = ["json", "markdown", "text", "srt"];

    private readonly M3Host _m3 = new();

    public ExportTests() => Destination = _m3.Directory.File("Exports");

    private string Destination { get; }

    public void Dispose() => _m3.Dispose();

    [Fact]
    public async Task TheEstimateListsTheFilesAndWhatIsUnavailable()
    {
        var id = await _m3.RecordAsync();

        var estimate = await _m3.ResultAsync("export.estimate", new { recordingId = id, selection = Everything() });

        Assert.Equal("files,bytes,items,unavailable", string.Join(",", estimate.EnumerateObject().Select(p => p.Name)));
        Assert.Equal(
            """[{"component":"transcript","reason":"Not transcribed yet"},{"component":"documents","reason":"Documents are not available in this build"},{"component":"attachments","reason":"No attachments"}]""",
            estimate.GetProperty("unavailable").GetRawText());
        var items = estimate.GetProperty("items").EnumerateArray().ToList();
        var baseName = await BaseNameAsync(id);
        Assert.Equal([baseName + ".flac", baseName + " - Simulated microphone.wav", baseName + " - details.json"], items.Select(i => i.GetProperty("name").GetString()));
        Assert.Equal(["audioMixed", "tracks", "details"], items.Select(i => i.GetProperty("component").GetString()));

        // The totals add manifest.json, which belongs to no row.
        Assert.Equal(items.Count + 1, estimate.GetProperty("files").GetInt32());
        Assert.True(estimate.GetProperty("bytes").GetInt64() > items.Sum(i => i.GetProperty("bytes").GetInt64()));

        var mix = new FileInfo(Path.Combine(_m3.Host.Store.GetProjectFolder(id), "mix.flac")).Length;
        Assert.Equal(mix, items[0].GetProperty("bytes").GetInt64());
    }

    [Theory]
    [InlineData("Design review: library screen", "Design review - library screen 2026-10-05")]
    [InlineData("A/B test | results?", "A - B test - results 2026-10-05")]
    [InlineData("  --Roadmap--  ", "Roadmap 2026-10-05")]
    [InlineData("...", "Recording 2026-10-05")]
    public void TheFolderNameMatchesTheUi(string title, string expected)
    {
        Assert.Equal(expected, ExportNaming.BaseName(title, new DateTimeOffset(2026, 10, 5, 23, 30, 0, TimeSpan.FromHours(-6))));
        Assert.Equal(ExportNaming.MaxTitleLength + 11, ExportNaming.BaseName(new string('x', 120), DateTimeOffset.Now).Length);
    }

    [Fact]
    public async Task AnExportWritesEveryFileWithAManifestAndLeavesTheProjectAlone()
    {
        var id = await _m3.RecordAsync();
        _m3.WriteTranscript(id);
        await _m3.ResultPickingAsync("attachments.add", _m3.WriteFile("agenda.pdf", "%PDF-agenda"), new { recordingId = id });
        var before = Snapshot(id);

        var payload = await RunAsync(id, Everything(), createSubfolder: true);

        Assert.Equal("done", payload.GetProperty("state").GetString());
        Assert.Equal(100, payload.GetProperty("percent").GetInt32());
        var baseName = await BaseNameAsync(id);
        var folder = Path.Combine(Destination, baseName);
        Assert.Equal(folder, payload.GetProperty("outputFolder").GetString());
        var files = Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).Order(StringComparer.Ordinal).ToList();
        Assert.Equal(
            new[]
            {
                "Attachments/agenda.pdf", baseName + " - Simulated microphone.wav", baseName + " - details.json", baseName + " - transcript.json",
                baseName + " - transcript.md", baseName + " - transcript.txt", baseName + ".flac", baseName + ".srt", "manifest.json",
            }.Order(StringComparer.Ordinal),
            files);
        Assert.Equal(files.Count, payload.GetProperty("files").GetInt32());

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "manifest.json")));
        var root = manifest.RootElement;
        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("0.1.0", root.GetProperty("mementoVersion").GetString());
        Assert.Equal(id, root.GetProperty("recordingId").GetString());
        Assert.Equal("Weekly sync", root.GetProperty("title").GetString());
        Assert.True(root.TryGetProperty("exportedAt", out _));
        var listed = root.GetProperty("files").EnumerateArray().ToList();
        Assert.Equal(files.Count - 1, listed.Count);
        foreach (var file in listed)
        {
            var path = Path.Combine(folder, file.GetProperty("name").GetString()!);
            Assert.Equal(new FileInfo(path).Length, file.GetProperty("bytes").GetInt64());
            Assert.Equal(await FileHashes.Sha256Async(path, CancellationToken.None), file.GetProperty("sha256").GetString());
        }

        // Copies of the stored FLAC and the attachment are byte for byte.
        Assert.Equal(File.ReadAllBytes(Path.Combine(_m3.Host.Store.GetProjectFolder(id), "mix.flac")), File.ReadAllBytes(Path.Combine(folder, baseName + ".flac")));
        Assert.Equal("%PDF-agenda", File.ReadAllText(Path.Combine(folder, "Attachments", "agenda.pdf")));

        // The WAV track is decoded to 24-bit PCM.
        Assert.Equal(24, WavInfo.Read(Path.Combine(folder, baseName + " - Simulated microphone.wav")).Format.BitsPerSample);

        using var transcript = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, baseName + " - transcript.json")));
        Assert.Equal(3, transcript.RootElement.GetProperty("segments").GetArrayLength());
        Assert.Equal(root.GetProperty("exportedAt").GetString(), transcript.RootElement.GetProperty("exportedAt").GetString());
        Assert.Equal("Weekly sync", transcript.RootElement.GetProperty("recording").GetProperty("title").GetString());
        Assert.Equal("meeting", transcript.RootElement.GetProperty("recording").GetProperty("details").GetProperty("type").GetString());

        using var details = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, baseName + " - details.json")));
        Assert.Equal(id, details.RootElement.GetProperty("summary").GetProperty("id").GetString());
        Assert.True(details.RootElement.GetProperty("history").GetArrayLength() > 0);
        Assert.Equal("Simulated microphone", details.RootElement.GetProperty("tracks")[0].GetProperty("name").GetString());
        Assert.False(details.RootElement.TryGetProperty("mixUrl", out _));
        Assert.Equal("sha256", details.RootElement.GetProperty("integrity").GetProperty("algorithm").GetString());

        Assert.Empty(Directory.GetDirectories(folder, ".memento-export-*"));
        Assert.Equal(before, Snapshot(id));
    }

    [Fact]
    public async Task TheTranscriptTextOptionsShapeTheFilesAndTheManifestRecordsThem()
    {
        var id = await _m3.RecordAsync();
        _m3.WriteTranscript(id);
        var options = new TranscriptTextOptions { Timestamps = false, Speakers = false, Layout = "lines" };
        var selection = new ExportSelection { Transcript = new ExportTranscriptChoice { On = true, Formats = ["text", "markdown", "srt"], Options = options } };

        var payload = await RunAsync(id, selection, createSubfolder: false);

        Assert.Equal("done", payload.GetProperty("state").GetString());
        var baseName = await BaseNameAsync(id);
        var text = File.ReadAllText(Path.Combine(Destination, baseName + " - transcript.txt"));
        Assert.EndsWith("\r\nWelcome everyone to the planning meeting.\r\nThanks. Let's review the budget first.\r\nThe budget for the third quarter is approved.\r\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Speaker", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[0:", text, StringComparison.Ordinal);
        var markdown = File.ReadAllText(Path.Combine(Destination, baseName + " - transcript.md"));
        Assert.EndsWith("\r\n\r\nWelcome everyone to the planning meeting.\r\n\r\nThanks. Let's review the budget first.\r\n\r\nThe budget for the third quarter is approved.\r\n", markdown, StringComparison.Ordinal);

        // SRT keeps its own structure: cue times and speakers.
        Assert.Contains("Speaker 1: Welcome everyone", File.ReadAllText(Path.Combine(Destination, baseName + ".srt")), StringComparison.Ordinal);

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Destination, "manifest.json")));
        Assert.Equal("""{"timestamps":false,"speakers":false,"layout":"lines"}""", manifest.RootElement.GetProperty("transcriptOptions").GetRawText().Replace(" ", string.Empty, StringComparison.Ordinal).Replace("\r\n", string.Empty, StringComparison.Ordinal).Replace("\n", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public async Task AManifestWithoutAReadableTranscriptHasNoTranscriptOptions()
    {
        var id = await _m3.RecordAsync();
        _m3.WriteTranscript(id);
        var selection = new ExportSelection { Transcript = new ExportTranscriptChoice { On = true, Formats = ["json"] } };

        await RunAsync(id, selection, createSubfolder: false);

        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Destination, "manifest.json")));
        Assert.Equal(JsonValueKind.Null, manifest.RootElement.GetProperty("transcriptOptions").ValueKind);
    }

    [Fact]
    public async Task AnExportWithAnUnknownLayoutIsRefusedBeforeAnythingIsWritten()
    {
        var id = await _m3.RecordAsync();
        _m3.WriteTranscript(id);
        var selection = new { audioMixed = new { on = false, format = "flac", bitrateKbps = (int?)null }, tracks = new { on = false, format = "flac", bitrateKbps = (int?)null }, transcript = new { on = true, formats = new[] { "text" }, options = new { layout = "pages" } }, documents = new { on = false, documentIds = Array.Empty<string>(), format = "docx" }, details = new { on = false }, attachments = new { on = false } };

        var error = await _m3.ErrorAsync("export.run", new { recordingId = id, selection, destination = new { folder = Destination, createSubfolder = false }, remember = false });

        Assert.Equal("bridge.invalidParams", error.GetProperty("code").GetString());
        Assert.Contains("Transcript layout 'pages'", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.False(Directory.Exists(Destination) && Directory.EnumerateFileSystemEntries(Destination).Any());
    }

    [Fact]
    public async Task ExportingAgainNeverOverwrites()
    {
        var id = await _m3.RecordAsync();
        var selection = new ExportSelection { AudioMixed = new ExportAudioChoice { On = true }, Details = new ExportToggle { On = true } };
        var baseName = await BaseNameAsync(id);

        await RunAsync(id, selection, createSubfolder: false);
        File.WriteAllText(Path.Combine(Destination, baseName + " - details.json"), "mine");
        await RunAsync(id, selection, createSubfolder: false);
        await RunAsync(id, selection, createSubfolder: true);
        var again = await RunAsync(id, selection, createSubfolder: true);

        Assert.Equal("mine", File.ReadAllText(Path.Combine(Destination, baseName + " - details.json")));
        Assert.True(File.Exists(Path.Combine(Destination, baseName + " - details (2).json")));
        Assert.True(File.Exists(Path.Combine(Destination, baseName + " (2).flac")));
        Assert.True(File.Exists(Path.Combine(Destination, "manifest (2).json")));
        Assert.Equal(Path.Combine(Destination, baseName + " (2)"), again.GetProperty("outputFolder").GetString());
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Destination, "manifest (2).json")));
        Assert.Contains(manifest.RootElement.GetProperty("files").EnumerateArray(), f => f.GetProperty("name").GetString() == baseName + " (2).flac");
    }

    [Fact]
    public async Task CancellingRemovesEverythingTheJobWrote()
    {
        var id = await _m3.RecordAsync();
        _m3.Mp3.Gate = new TaskCompletionSource();
        var selection = new ExportSelection { Details = new ExportToggle { On = true }, AudioMixed = new ExportAudioChoice { On = true, Format = "mp3", BitrateKbps = 128 } };

        var jobId = (await _m3.ResultAsync("export.run", Run(id, selection, createSubfolder: true))).GetProperty("jobId").GetString();
        await TestRecordings.WaitUntilAsync(() => _m3.Mp3.Calls > 0, "the MP3 encode to start");
        var status = await _m3.Host.ResultAsync("status.get");
        await _m3.ResultAsync("export.cancel", new { jobId });
        var payload = await FinishedAsync(jobId!);

        Assert.Equal("""{"active":true,"percent":0,"title":"Weekly sync"}""", status.GetProperty("export").GetRawText());
        Assert.Equal("cancelled", payload.GetProperty("state").GetString());
        Assert.Contains("removed", payload.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Destination));
        Assert.Equal("""{"active":false,"percent":null,"title":null}""", (await _m3.Host.ResultAsync("status.get")).GetProperty("export").GetRawText());
        Assert.Equal("{}", (await _m3.ResultAsync("export.cancel", new { jobId })).GetRawText());
    }

    [Fact]
    public async Task ARunningExportIsJournalledAndForgottenWhenItFinishes()
    {
        var id = await _m3.RecordAsync();
        _m3.Mp3.Gate = new TaskCompletionSource();
        var selection = new ExportSelection { Details = new ExportToggle { On = true }, AudioMixed = new ExportAudioChoice { On = true, Format = "mp3", BitrateKbps = 128 } };
        var journal = _m3.Host.Get<ExportJournal>();

        var jobId = (await _m3.ResultAsync("export.run", Run(id, selection, createSubfolder: true))).GetProperty("jobId").GetString();
        await TestRecordings.WaitUntilAsync(() => _m3.Mp3.Calls > 0, "the MP3 encode to start");

        var entry = Assert.Single(journal.Entries);
        Assert.Equal(jobId, entry.JobId);
        Assert.Equal(id, entry.RecordingId);
        Assert.StartsWith(Destination, entry.OutputFolder, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(entry.Work);
        _m3.Mp3.Gate.SetResult();
        await FinishedAsync(jobId!);
        Assert.Empty(journal.Entries);
    }

    [Fact]
    public async Task AnExportCutShortByACrashIsCleanedUpAtTheNextLaunch()
    {
        var id = await _m3.RecordAsync();
        var output = Path.Combine(Destination, "Weekly sync 2026-10-06");
        var work = Path.Combine(output, ".memento-export-xdead");
        Directory.CreateDirectory(work);
        File.WriteAllText(Path.Combine(work, "abc.part.wav"), "half");
        var written = Path.Combine(output, "Weekly sync 2026-10-06.wav");
        File.WriteAllText(written, "complete file of an incomplete export");
        _m3.Host.Get<ExportJournal>().Set(new ExportJournalEntry("xdead", id, "Weekly sync", DateTimeOffset.Now, output, work, [written], [output]));

        var cleaned = await _m3.Host.Get<InterruptedExports>().CleanUpAsync(CancellationToken.None);

        Assert.Equal(1, cleaned);
        Assert.False(Directory.Exists(output));
        Assert.Empty(_m3.Host.Get<ExportJournal>().Entries);
        var history = await _m3.Host.Store.ReadHistoryAsync(id, CancellationToken.None);
        var line = Assert.Single(history, h => h.Summary == "Export interrupted");
        Assert.Equal($"Memento closed while exporting to {output}. The 1 file it had written there was removed; nothing inside Memento was changed. Export again from Review.", line.Detail);
    }

    [Fact]
    public async Task AFailedFileIsNamedAndTheRestRemoved()
    {
        var id = await _m3.RecordAsync();
        var selection = new ExportSelection { Details = new ExportToggle { On = true }, Tracks = new ExportAudioChoice { On = true, Format = "mp3" } };
        Directory.CreateDirectory(Destination);
        File.WriteAllText(Path.Combine(Destination, "keep.txt"), "not ours");
        _m3.Mp3.Gate = new TaskCompletionSource();
        _m3.Mp3.Gate.SetException(new IOException("The encoder stopped"));

        var payload = await RunAsync(id, selection, createSubfolder: false);

        Assert.Equal("failed", payload.GetProperty("state").GetString());
        var message = payload.GetProperty("message").GetString()!;
        Assert.Contains("Simulated microphone.mp3\" could not be written: The encoder stopped", message, StringComparison.Ordinal);
        Assert.Contains("Nothing inside Memento was changed", message, StringComparison.Ordinal);
        Assert.Equal(["keep.txt"], Directory.EnumerateFileSystemEntries(Destination).Select(Path.GetFileName));
    }

    [Fact]
    public async Task UnwritableDestinationsAreRefusedBeforeAnythingIsWritten()
    {
        var id = await _m3.RecordAsync();
        var selection = new ExportSelection { Details = new ExportToggle { On = true } };
        var aFile = _m3.WriteFile("not-a-folder.txt", "x");

        var relative = await _m3.ErrorAsync("export.run", Run(id, selection, true, "Exports"));
        var inside = await _m3.ErrorAsync("export.run", Run(id, selection, true, Path.Combine(_m3.Host.Library.Root, "projects")));
        var file = await _m3.ErrorAsync("export.run", Run(id, selection, true, aFile));
        _m3.Host.FreeSpace.FreeBytes = 1000;
        var full = await _m3.ErrorAsync("export.run", Run(id, selection, true, Destination));

        Assert.All(new[] { relative, inside, file, full }, e => Assert.Equal(DomainErrorCodes.ExportDestinationUnwritable, e.GetProperty("code").GetString()));
        Assert.Contains("inside the Memento library", inside.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains("1000 bytes free", full.GetProperty("detail").GetString(), StringComparison.Ordinal);
        Assert.Contains("Nothing was written", full.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(Destination));
    }

    [Fact]
    public async Task NothingToExportAndUnknownJobsAreSpecificErrors()
    {
        var id = await _m3.RecordAsync();

        var nothing = await _m3.ErrorAsync("export.run", Run(id, new ExportSelection { Transcript = new ExportTranscriptChoice { On = true, Formats = AllTranscriptFormats } }, true));
        var unknownCancel = await _m3.ErrorAsync("export.cancel", new { jobId = "x123" });
        var unknownOpen = await _m3.ErrorAsync("export.openFolder", new { jobId = "x123" });
        var badFormat = await _m3.ErrorAsync("export.estimate", new { recordingId = id, selection = new ExportSelection { AudioMixed = new ExportAudioChoice { On = true, Format = "ogg" } } });
        var noProject = await _m3.ErrorAsync("export.estimate", new { recordingId = "20260101-000000-aaaaaa", selection = new ExportSelection() });

        Assert.Equal(DomainErrorCodes.ExportNothingSelected, nothing.GetProperty("code").GetString());
        Assert.Contains("Transcript: not transcribed yet", nothing.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.Equal(DomainErrorCodes.ExportNotFound, unknownCancel.GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.ExportNotFound, unknownOpen.GetProperty("code").GetString());
        Assert.Equal(BridgeErrorCodes.InvalidParams, badFormat.GetProperty("code").GetString());
        Assert.Equal(DomainErrorCodes.ProjectNotFound, noProject.GetProperty("code").GetString());
    }

    [Fact]
    public async Task RememberStoresTheDefaultsAndOpenFolderShowsTheOutput()
    {
        var id = await _m3.RecordAsync();
        var selection = new ExportSelection { Details = new ExportToggle { On = true }, Tracks = new ExportAudioChoice { On = true, Format = "mp3", BitrateKbps = 256 } };

        var jobId = (await _m3.ResultAsync("export.run", Run(id, selection, createSubfolder: false, remember: true))).GetProperty("jobId").GetString()!;
        var payload = await FinishedAsync(jobId);
        await _m3.ResultAsync("export.openFolder", new { jobId });

        var settings = _m3.Host.Settings.Current.Export;
        Assert.Equal(Destination, settings.DefaultFolder);
        Assert.False(settings.CreateSubfolder);
        Assert.True(settings.Defaults.Details.On);
        Assert.Equal(256, settings.Defaults.Tracks.BitrateKbps);
        Assert.Equal("done", payload.GetProperty("state").GetString());
        Assert.Equal(Destination, _m3.Host.Launcher.Opened.Single().LocalPath);
    }

    [Fact]
    public async Task ProgressNeverExceedsFourEventsASecond()
    {
        var id = await _m3.RecordAsync();
        _m3.WriteTranscript(id);

        var payload = await RunAsync(id, Everything(), createSubfolder: true);

        var events = _m3.Sink.Payloads(BridgeEventNames.ExportProgress);
        Assert.Equal("done", payload.GetProperty("state").GetString());
        Assert.True(events.Count >= 2);
        Assert.Equal("running", events[0].GetProperty("state").GetString());
        var throttle = new ProgressThrottle(TimeProvider.System);
        Assert.True(throttle.TryPass());
        Assert.False(throttle.TryPass());
        Assert.True(throttle.TryPass(force: true));
    }

    [Fact]
    public void MarkdownHasSpeakerParagraphsWithAMarkerPerSegment()
    {
        var transcript = TranscriptFixtures.Document(
            TranscriptFixtures.Segment("s1", 2, 4, "Welcome *everyone*.", "spk1"),
            TranscriptFixtures.Segment("s2", 4.5, 6, "Let's start.", "spk1"),
            TranscriptFixtures.Segment("s3", 3725, 3730, "Thanks [all].", "spk2"),
            TranscriptFixtures.Segment("s4", 3731, 3733, "No speaker here."));

        var markdown = TranscriptText.Markdown(transcript, Summary());

        Assert.StartsWith("# Weekly sync\r\n\r\n2026-10-06 10:00 · 1:02:13 · Speakers: Speaker 1, Speaker 2\r\n", markdown, StringComparison.Ordinal);
        Assert.Contains("\r\n**Speaker 1:** [0:00:02] Welcome \\*everyone\\*. [0:00:04] Let's start.\r\n\r\n**Speaker 2:** [1:02:05] Thanks \\[all\\].\r\n\r\n[1:02:11] No speaker here.\r\n", markdown, StringComparison.Ordinal);

        var text = TranscriptText.Plain(transcript, Summary());
        Assert.Contains("[0:00:04] Speaker 1: Let's start.\r\n", text, StringComparison.Ordinal);
        Assert.Contains("[1:02:11] No speaker here.\r\n", text, StringComparison.Ordinal);
    }

    [Fact]
    public void SrtCuesKeepToTwoLinesOf42CharactersAndNeverOverlap()
    {
        var longText = string.Join(' ', Enumerable.Repeat("The quarterly budget review covers travel, equipment and the new office lease.", 4));
        var transcript = TranscriptFixtures.Document(
            TranscriptFixtures.Segment("s1", 1.25, 3.5, "Welcome everyone.", "spk1"),
            TranscriptFixtures.Segment("s2", 3.4, 40, longText, "spk2"),
            TranscriptFixtures.Segment("s3", 40, 40.1, "Ok.") with { Words = [] },
            TranscriptFixtures.Segment("s4", 41, 43, "Supercalifragilisticexpialidocious-and-even-longer-compound-word", "spk1"));

        var srt = SrtWriter.Write(transcript);

        var cues = srt.Split("\r\n\r\n", StringSplitOptions.RemoveEmptyEntries).Select(c => c.Split("\r\n")).ToList();
        Assert.Equal(["1", "00:00:01,250 --> 00:00:03,500", "Speaker 1: Welcome everyone."], cues[0]);
        Assert.StartsWith("Speaker 2: The quarterly", cues[1][2], StringComparison.Ordinal);
        Assert.True(cues.Count > 6);
        var previousEnd = TimeSpan.Zero;
        for (var i = 0; i < cues.Count; i++)
        {
            Assert.Equal((i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), cues[i][0]);
            var lines = cues[i][2..];
            Assert.InRange(lines.Length, 1, SrtWriter.MaxLines);
            Assert.All(lines, line => Assert.InRange(line.Length, 1, SrtWriter.MaxLineLength));
            var times = cues[i][1].Split(" --> ").Select(t => TimeSpan.ParseExact(t, @"hh\:mm\:ss\,fff", System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            Assert.True(times[0] >= previousEnd, $"cue {i + 1} starts before the previous one ends");
            Assert.True(times[1] > times[0]);
            previousEnd = times[1];
        }

        // Every word survives the split, in order.
        var ok = cues.FindIndex(c => c[2] == "Ok.");
        var words = string.Join(' ', cues.Skip(1).Take(ok - 1).SelectMany(c => c[2..])).Replace("Speaker 2: ", string.Empty, StringComparison.Ordinal);
        Assert.Equal(longText, words);
        Assert.Equal("00:00:40,000 --> 00:00:40,500", cues[ok][1]);
        Assert.Equal("01:02:03,457", SrtWriter.Time(3723.4567));
    }

    [Fact]
    public async Task ACraftedProjectJsonCannotWriteOrReadOutsideItsFolders()
    {
        var id = await _m3.RecordAsync();
        var folder = _m3.Host.Store.GetProjectFolder(id);
        Directory.CreateDirectory(Path.Combine(folder, "attachments"));
        File.WriteAllText(Path.Combine(folder, "attachments", "agenda.txt"), "agenda");
        var outside = _m3.Directory.File("outside-secret.txt");
        File.WriteAllText(outside, "not part of the recording");
        await _m3.Host.Store.UpdateAsync(
            id,
            m => m with
            {
                Attachments =
                [
                    new AttachmentRecord { Id = "a1", Name = @"..\..\..\Startup\evil.bat", File = "attachments/agenda.txt", SizeBytes = 6 },
                ],
            },
            CancellationToken.None);

        var payload = await RunAsync(id, new ExportSelection { Attachments = new ExportToggle { On = true } }, createSubfolder: true);

        Assert.Equal("done", payload.GetProperty("state").GetString());
        var written = Directory.EnumerateFiles(_m3.Directory.Path, "*", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
            .Select(f => Path.GetRelativePath(_m3.Directory.Path, f))
            .ToList();
        Assert.DoesNotContain(written, f => f.EndsWith("evil.bat", StringComparison.OrdinalIgnoreCase) && !f.StartsWith("Exports", StringComparison.OrdinalIgnoreCase));
        var exported = Directory.EnumerateFiles(Destination, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToList();
        Assert.Contains("evil.bat", exported); // the name, made safe, inside the export's Attachments folder
        Assert.All(Directory.EnumerateFiles(Destination, "*", SearchOption.AllDirectories), f => Assert.StartsWith(Destination, f, StringComparison.OrdinalIgnoreCase));

        // A file named outside the project folder makes the whole manifest untrusted (ProjectPaths): nothing is exported.
        await _m3.Host.Store.UpdateAsync(
            id,
            m => m with { Attachments = [.. m.Attachments, new AttachmentRecord { Id = "a2", Name = "secret.txt", File = "../../../outside-secret.txt", SizeBytes = 25 }] },
            CancellationToken.None);
        await _m3.ErrorAsync("export.run", Run(id, new ExportSelection { Attachments = new ExportToggle { On = true } }, createSubfolder: true));
        await _m3.Get<ExportService>().WhenIdleAsync();
        Assert.DoesNotContain("secret.txt", Directory.EnumerateFiles(Destination, "*", SearchOption.AllDirectories).Select(Path.GetFileName)); // never read
    }

    private static RecordingSummary Summary() =>
        new("r1", "Weekly sync", "meeting", new DateTimeOffset(2026, 10, 6, 10, 0, 0, TimeSpan.FromHours(1)), 3_733_000, 2, false, [], [], false, "ready", 0);

    private static ExportSelection Everything() => new()
    {
        AudioMixed = new ExportAudioChoice { On = true, Format = "flac" },
        Tracks = new ExportAudioChoice { On = true, Format = "wav" },
        Transcript = new ExportTranscriptChoice { On = true, Formats = AllTranscriptFormats },
        Documents = new ExportDocumentsChoice { On = true },
        Details = new ExportToggle { On = true },
        Attachments = new ExportToggle { On = true },
    };

    private object Run(string id, ExportSelection selection, bool createSubfolder, string? folder = null, bool remember = false) =>
        new { recordingId = id, selection, destination = new { folder = folder ?? Destination, createSubfolder }, remember };

    private async Task<JsonElement> RunAsync(string id, ExportSelection selection, bool createSubfolder)
    {
        var jobId = (await _m3.ResultAsync("export.run", Run(id, selection, createSubfolder))).GetProperty("jobId").GetString()!;
        return await FinishedAsync(jobId);
    }

    private async Task<JsonElement> FinishedAsync(string jobId)
    {
        await _m3.Get<ExportService>().WhenIdleAsync();
        return await _m3.Sink.WaitForAsync(
            BridgeEventNames.ExportProgress,
            p => p.GetProperty("jobId").GetString() == jobId && p.GetProperty("state").GetString() != "running");
    }

    private async Task<string> BaseNameAsync(string id)
    {
        var manifest = await _m3.Host.Store.LoadAsync(id, CancellationToken.None);
        return ExportNaming.BaseName(manifest.Details.Title, manifest.CreatedAt);
    }

    /// <summary>Every file of the project with its bytes, to show an export changed nothing.</summary>
    private Dictionary<string, string> Snapshot(string id)
    {
        var folder = _m3.Host.Store.GetProjectFolder(id);
        return Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(folder, f), f => Convert.ToBase64String(File.ReadAllBytes(f)));
    }
}
