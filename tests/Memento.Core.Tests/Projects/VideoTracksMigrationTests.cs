using System.Text.Json;
using System.Text.Json.Nodes;
using Memento.Core.Projects;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Projects;

/// <summary>Schema v4 (2.0): the reserved <c>videoTracks</c> becomes typed; damaged entries are dropped, readable ones kept as they are.</summary>
public sealed class VideoTracksMigrationTests : IDisposable
{
    private const string ScreenTrack = """
        {
          "id": "screen", "kind": "video", "role": "screen", "sourceKind": "display", "sourceId": "display:\\\\.\\DISPLAY1",
          "name": "Display 1", "file": "tracks/screen.mp4",
          "parts": [ { "file": "tracks/screen.mp4", "startOffsetMs": 120, "durationMs": 20000, "encoder": "Test encoder", "hardwareEncoder": true, "framesWritten": 600 } ],
          "width": 1920, "height": 1080, "frameRate": 30, "startOffsetMs": 120, "durationMs": 20000, "framesWritten": 600, "framesDropped": 1,
          "laterField": "kept"
        }
        """;

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void A1xManifestWithTheReservedEmptyListMigratesToV4Unchanged()
    {
        var migrated = ProjectManifestMigrator.Default.Migrate(new JsonObject { ["schemaVersion"] = 3, ["videoTracks"] = new JsonArray() });

        Assert.Equal(4, migrated.Manifest["schemaVersion"]!.GetValue<int>());
        Assert.Empty(migrated.Manifest["videoTracks"]!.AsArray());
        Assert.True(migrated.Migrated);
    }

    [Fact]
    public void ReadableVideoTracksAreKeptAndDamagedOnesDropped()
    {
        var manifest = new JsonObject
        {
            ["schemaVersion"] = 3,
            ["videoTracks"] = JsonNode.Parse($$"""
                [
                  {{ScreenTrack}},
                  { "id": "camera", "kind": "video", "role": "camera", "sourceKind": "camera", "sourceId": "camera:x", "name": "Cam" },
                  { "id": "audio-ish", "kind": "audio", "role": "screen", "sourceKind": "display", "sourceId": "d", "name": "n", "file": "f.mp4" },
                  { "id": "other", "kind": "video", "role": "projector", "sourceKind": "display", "sourceId": "d", "name": "n", "file": "f.mp4" },
                  { "id": "typed-wrong", "kind": "video", "role": "screen", "sourceKind": "display", "sourceId": "d", "name": "n", "file": "f.mp4", "width": "wide" },
                  "not an object"
                ]
                """),
        };

        var migrated = ProjectManifestMigrator.Default.Migrate(manifest);

        var kept = Assert.Single(migrated.Manifest["videoTracks"]!.AsArray());
        Assert.Equal("screen", kept!["id"]!.GetValue<string>());
        Assert.Equal("kept", kept["laterField"]!.GetValue<string>());
    }

    [Fact]
    public void AVideoTracksFieldThatIsNotAListBecomesEmpty()
    {
        var migrated = ProjectManifestMigrator.Default.Migrate(new JsonObject { ["schemaVersion"] = 3, ["videoTracks"] = "screen.mp4" });

        Assert.Empty(migrated.Manifest["videoTracks"]!.AsArray());
    }

    [Fact]
    public async Task AV3ProjectOpensWithTypedVideoTracksAndIsWrittenAsV4KeepingUnknownFields()
    {
        var store = new ProjectStore(new FakeLibraryLocation(_directory.Path), TimeProvider.System, NullLogger<ProjectStore>.Instance, ProjectManifestMigrator.Default);
        var created = await store.CreateAsync(new ProjectCreateRequest("Screen share", "meeting", DateTimeOffset.Now, ProjectStates.Ready), CancellationToken.None);
        var path = Path.Combine(store.GetProjectFolder(created.Id), "project.json");
        var node = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        node["schemaVersion"] = 3;
        node["videoTracks"] = JsonNode.Parse($"[{ScreenTrack}]");
        await File.WriteAllTextAsync(path, node.ToJsonString());

        var loaded = await store.LoadAsync(created.Id, CancellationToken.None);
        await store.SaveAsync(loaded, CancellationToken.None);

        var track = Assert.Single(loaded.VideoTracks);
        Assert.Equal(("screen", "video", 120L, 600L), (track.Role, track.Kind, track.StartOffsetMs, track.FramesWritten));
        Assert.Null(loaded.VideoRemoved);
        var written = JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
        Assert.Equal(4, written["schemaVersion"]!.GetValue<int>());
        Assert.Equal("kept", written["videoTracks"]![0]!["laterField"]!.GetValue<string>());
    }

    [Fact]
    public void TheRecordingStateKeepsVideoCheckpointsAndReadsOldFilesWithoutThem()
    {
        var state = new RecordingStateDocument
        {
            SessionId = "s",
            RecordingId = "r",
            VideoTracks = [new VideoTrackCheckpoint("screen", "screen", "display", @"display:\\.\DISPLAY1", "Display 1", 1920, 1080, 30, [new VideoTrackPart("tracks/screen.mp4", 0, 5000, "enc", true, 150)], 1234, 150, 0, null, null)],
        };

        var json = JsonSerializer.Serialize(state, ProjectJsonContext.Default.RecordingStateDocument);
        var back = JsonSerializer.Deserialize(json, ProjectJsonContext.Default.RecordingStateDocument)!;
        var old = JsonSerializer.Deserialize("""{ "schemaVersion": 1, "sessionId": "s", "recordingId": "r" }""", ProjectJsonContext.Default.RecordingStateDocument)!;

        Assert.Equal("tracks/screen.mp4", Assert.Single(Assert.Single(back.VideoTracks).Parts).File);
        Assert.Empty(old.VideoTracks);
    }
}
