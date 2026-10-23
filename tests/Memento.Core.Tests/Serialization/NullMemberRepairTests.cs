using System.Text.Json.Nodes;
using Memento.Core.Library;
using Memento.Core.Models;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;
using Memento.Core.Transcripts;
using Memento.Core.Video;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Serialization;

/// <summary>
/// Security audit 2, §9, file by file, as MissingFieldsKeepDefaultsTests does for settings.json: a list or object a file
/// leaves out, or writes as <c>null</c>, reads as its default; a null list entry is dropped; and the few members that
/// have no default make the file unreadable by name rather than reach the code as null.
/// </summary>
public sealed class NullMemberRepairTests : IDisposable
{
    private static readonly string[] LeftOut = ["state", "details", "stages", "integrity", "failures", "pauses"];

    private readonly TempDirectory _directory = new();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.Zero));
    private readonly ProjectStore _projects;

    public NullMemberRepairTests()
    {
        _projects = new ProjectStore(new FakeLibraryLocation(_directory.Path), _time, NullLogger<ProjectStore>.Instance);
    }

    public void Dispose() => _directory.Dispose();

    [Fact]
    public async Task AManifestWithoutItsListsAndObjectsReadsWithTheirDefaults()
    {
        var id = await ProjectAsync();
        await RewriteAsync(id, ProjectLayout.ManifestFile, node =>
        {
            foreach (var name in LeftOut)
            {
                node.Remove(name);
            }
        });

        var manifest = await _projects.LoadAsync(id, CancellationToken.None);

        Assert.Equal(ProjectStates.Recording, manifest.State);
        Assert.Equal(new ProjectDetails().Type, manifest.Details.Type);
        Assert.Empty(manifest.Details.Participants);
        Assert.Empty(manifest.Details.Agenda.Items);
        Assert.Empty(manifest.Details.WhoSpoke.Names);
        Assert.Empty(manifest.Stages);
        Assert.Empty(manifest.Integrity.Files);
        Assert.Empty(manifest.Failures);
        Assert.Empty(manifest.Pauses);
    }

    [Fact]
    public async Task AManifestWithNullListsAndObjectsReadsWithTheirDefaultsAndDropsNullEntries()
    {
        var id = await ProjectAsync();
        await RewriteAsync(id, ProjectLayout.ManifestFile, node =>
        {
            node["details"] = new JsonObject { ["title"] = "Kept", ["participants"] = null, ["tags"] = new JsonArray(null, "board"), ["agenda"] = null, ["whoSpoke"] = new JsonObject { ["count"] = 2, ["names"] = null, ["marks"] = null } };
            node["stages"] = null;
            node["integrity"] = null;
            node["failures"] = new JsonArray((JsonNode?)null);
            node["videoPip"] = null;
            node["segments"] = new JsonArray((JsonNode?)null);
        });

        var manifest = await _projects.LoadAsync(id, CancellationToken.None);

        Assert.Equal("Kept", manifest.Details.Title);
        Assert.Empty(manifest.Details.Participants);
        Assert.Equal(["board"], manifest.Details.Tags);
        Assert.Empty(manifest.Details.Agenda.Items);
        Assert.Equal(2, manifest.Details.WhoSpoke.Count);
        Assert.Empty(manifest.Details.WhoSpoke.Names);
        Assert.Empty(manifest.Details.WhoSpoke.Marks);
        Assert.Empty(manifest.Stages);
        Assert.Equal("sha256", manifest.Integrity.Algorithm);
        Assert.Empty(manifest.Failures);
        Assert.Empty(manifest.VideoPip);
        Assert.Empty(manifest.Segments);
        Assert.Null(Memento.Core.Serialization.NullMembers.FindFirst(manifest));
    }

    [Fact]
    public async Task ANullTrackEntryIsStillRefusedLikeAnUnsafePath()
    {
        // SA2-08: tracks, video tracks and attachments name files; a null entry there refuses the manifest, as before.
        var id = await ProjectAsync();
        await RewriteAsync(id, ProjectLayout.ManifestFile, node => node["tracks"] = new JsonArray((JsonNode?)null));

        var error = await Assert.ThrowsAsync<ProjectNotFoundException>(() => _projects.LoadAsync(id, CancellationToken.None));

        Assert.Contains("tracks[0]", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnnotationsWithNullListsReadAsEmpty()
    {
        var id = await ProjectAsync();
        await WriteAsync(id, ProjectLayout.AnnotationsFile, """{ "schemaVersion": 1, "chapters": null, "highlights": [null], "dismissedSuggestions": null }""");

        var annotations = await _projects.LoadAnnotationsAsync(id, CancellationToken.None);

        Assert.Empty(annotations.Chapters);
        Assert.Empty(annotations.Highlights);
        Assert.Empty(annotations.Topics);
        Assert.Empty(annotations.DismissedSuggestions);
    }

    [Fact]
    public async Task ARecordingStateWithNullListsIsReadForRecoveryInsteadOfThrowing()
    {
        // Before 2.4.0 the path check after reading threw NullReferenceException on "tracks": null.
        var id = await ProjectAsync();
        await WriteAsync(id, ProjectLayout.RecordingStateFile, $$"""
            { "schemaVersion": 1, "sessionId": "s1", "recordingId": "{{id}}", "tracks": null, "pauses": [null],
              "videoTracks": [ { "trackId": "screen", "role": "screen", "sourceKind": "display", "sourceId": "d1", "name": "Display 1", "parts": null }, null ] }
            """);

        var state = await _projects.ReadRecordingStateAsync(id, CancellationToken.None);

        Assert.NotNull(state);
        Assert.Equal("recording", state.State);
        Assert.Empty(state.Tracks);
        Assert.Empty(state.Pauses);
        var video = Assert.Single(state.VideoTracks);
        Assert.Empty(video.Parts);
    }

    [Fact]
    public async Task AFoldersFileWithANullListReadsAsNoFolders()
    {
        Directory.CreateDirectory(_directory.Path);
        await File.WriteAllTextAsync(Path.Combine(_directory.Path, "folders.json"), """{ "schemaVersion": 1, "folders": null }""");
        var store = new FolderStore(new FakeLibraryLocation(_directory.Path), NullLogger<FolderStore>.Instance);

        Assert.Empty(await store.LoadAsync(CancellationToken.None));
    }

    [Fact]
    public async Task ATranscriptWithNullListsAndAnOldEngineBlockReadsWithDefaults()
    {
        var id = await ProjectAsync();
        await WriteAsync(id, ProjectLayout.TranscriptFile, """
            { "schemaVersion": 1, "engine": null, "speakers": null, "coverageGaps": null,
              "segments": [ null, { "id": "s1", "start": 0, "end": 1.5, "text": "Good morning.", "confidence": 0.9, "words": null } ] }
            """);
        var store = new TranscriptStore(_projects, _time, NullLogger<TranscriptStore>.Instance);

        var transcript = await store.LoadAsync(id, CancellationToken.None);

        Assert.NotNull(transcript);
        Assert.Equal("whisper.cpp", transcript.Engine.Name);
        Assert.Equal("en", transcript.Language);
        Assert.True(transcript.Complete);
        Assert.Equal(0.5, transcript.LowConfidenceThreshold);
        Assert.Empty(transcript.Speakers);
        Assert.Empty(transcript.CoverageGaps);
        var segment = Assert.Single(transcript.Segments);
        Assert.Empty(segment.Words);
    }

    [Fact]
    public async Task ATranscriptVersionWithoutItsTranscriptIsUnreadableNotEmpty()
    {
        // Restoring it would replace the transcript with nothing; it is left out of the list and cannot be loaded.
        var id = await ProjectAsync();
        var folder = Path.Combine(_projects.GetProjectFolder(id), ProjectLayout.VersionsFolder);
        Directory.CreateDirectory(folder);
        await File.WriteAllTextAsync(Path.Combine(folder, "transcript.20261022T090000000Z.json"), """{ "schemaVersion": 1, "id": "20261022T090000000Z", "savedAt": "2026-10-22T09:00:00+00:00", "reason": "edited", "transcript": null }""");
        var store = new TranscriptStore(_projects, _time, NullLogger<TranscriptStore>.Instance);

        Assert.Empty(await store.ListVersionsAsync(id, CancellationToken.None));
        Assert.Null(await store.LoadVersionAsync(id, "20261022T090000000Z", CancellationToken.None));
    }

    [Fact]
    public async Task VoicesWithoutClustersAreStillNotUsed()
    {
        var id = await ProjectAsync();
        await WriteAsync(id, ProjectLayout.VoicesFile, """{ "schemaVersion": 1, "embeddingModelId": "m", "signature": "s", "tracks": [], "clusters": null }""");
        var store = new TranscriptStore(_projects, _time, NullLogger<TranscriptStore>.Instance);

        Assert.Null(await store.LoadVoicesAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task APartialTranscriptWithNullListsResumesFromNothingDone()
    {
        var id = await ProjectAsync();
        await File.WriteAllTextAsync(Path.Combine(_projects.GetProjectFolder(id), ProjectLayout.TranscriptPartialFile), """{ "schemaVersion": 1, "signature": "s", "tracks": null, "segments": null, "elapsedMs": 0 }""");
        var store = new TranscriptStore(_projects, _time, NullLogger<TranscriptStore>.Instance);

        var partial = await store.LoadPartialAsync(id, CancellationToken.None);

        Assert.NotNull(partial);
        Assert.Empty(partial.Tracks);
        Assert.Empty(partial.Segments);
    }

    [Theory]
    [InlineData("""{ "schemaVersion": 1, "models": null }""", "'models'")]
    [InlineData("""{ "schemaVersion": 1, "models": [null] }""", "'models[0]'")]
    public void ACatalogWithANullListIsRefusedByName(string json, string member)
    {
        var error = Assert.Throws<InvalidDataException>(() => ModelCatalog.Parse(json));

        Assert.Contains(member, error.Message, StringComparison.Ordinal);
        Assert.Contains("is null", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACatalogWithoutModelsIsEmptyNotNull() => Assert.Empty(ModelCatalog.Parse("""{ "schemaVersion": 1 }""").Entries);

    [Theory]
    [InlineData("""{ "height": 1080 }""")]
    [InlineData("""{ "roles": null, "height": 1080 }""")]
    [InlineData("""{ "roles": [null], "height": 1080 }""")]
    public void TheLibraryIndexsVideoSummaryNeverHasNullRoles(string json)
    {
        var summary = VideoMapper.FromJson(json);

        Assert.NotNull(summary);
        Assert.Empty(summary.Roles);
        Assert.Equal(1080, summary.Height);
    }

    private async Task<string> ProjectAsync()
    {
        var manifest = await _projects.CreateAsync(new ProjectCreateRequest("Null members", "meeting", _time.GetLocalNow(), ProjectStates.Ready), CancellationToken.None);
        return manifest.Id;
    }

    private Task WriteAsync(string id, string file, string json) => File.WriteAllTextAsync(Path.Combine(_projects.GetProjectFolder(id), file), json);

    private async Task RewriteAsync(string id, string file, Action<JsonObject> change)
    {
        var path = Path.Combine(_projects.GetProjectFolder(id), file);
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        change(node);
        await File.WriteAllTextAsync(path, node.ToJsonString());
    }
}
