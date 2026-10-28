using System.Text.Json.Nodes;
using Memento.Core.Projects;
using Memento.Core.Video;

namespace Memento.Core.Tests.Projects;

/// <summary>Schema v8 (2.8.0): every video track gets its own <c>stoppedAtMs</c>, so its time range is stored.</summary>
public sealed class VideoRangesMigrationTests
{
    [Fact]
    public void AV6FileGoesThroughBothStepsAndATrackStopsWhereItEndedEarlyOrAtTheEndOfItsFrames()
    {
        var manifest = new JsonObject
        {
            ["schemaVersion"] = 6,
            ["videoTracks"] = JsonNode.Parse("""
                [
                  { "id": "screen", "startOffsetMs": 120, "durationMs": 20000 },
                  { "id": "screen-2", "startOffsetMs": 5000, "durationMs": 3000, "endedEarlyAtMs": 8200, "endReason": "disabled" },
                  { "id": "camera", "startOffsetMs": 0, "durationMs": 10, "stoppedAtMs": 42 },
                  { "id": "odd" },
                  "not an object"
                ]
                """),
        };

        var migrated = ProjectManifestMigrator.Default.Migrate(manifest);

        Assert.Equal(8, migrated.Manifest["schemaVersion"]!.GetValue<int>());
        var tracks = migrated.Manifest["videoTracks"]!.AsArray();
        Assert.Equal(20_120L, tracks[0]!["stoppedAtMs"]!.GetValue<long>());
        Assert.Equal(8200L, tracks[1]!["stoppedAtMs"]!.GetValue<long>());
        Assert.Equal(42L, tracks[2]!["stoppedAtMs"]!.GetValue<long>());
        Assert.Equal(0L, tracks[3]!["stoppedAtMs"]!.GetValue<long>());
        Assert.Equal("not an object", tracks[4]!.GetValue<string>());
    }

    [Fact]
    public void AV7FileTakesOnlyTheVideoRangesStep()
    {
        var migrated = ProjectManifestMigrator.Default.Migrate(new JsonObject
        {
            ["schemaVersion"] = 7,
            ["videoTracks"] = JsonNode.Parse("""[ { "id": "screen", "startOffsetMs": 10, "durationMs": 90 } ]"""),
        });

        Assert.Equal((8, true), (migrated.Manifest["schemaVersion"]!.GetValue<int>(), migrated.Migrated));
        Assert.Equal(100L, migrated.Manifest["videoTracks"]![0]!["stoppedAtMs"]!.GetValue<long>());
    }

    [Fact]
    public void AManifestWithoutVideoIsLeftAsItIs()
    {
        var migrated = VideoRangesMigration.Upgrade(new JsonObject { ["videoTracks"] = "broken" });
        Assert.Equal("broken", migrated["videoTracks"]!.GetValue<string>());
        Assert.Empty(VideoRangesMigration.Upgrade([]));
    }

    [Fact]
    public void ReviewReadsTheStoredRangeAndFallsBackForATrackStillRecording()
    {
        var stored = new VideoTrack { Id = "screen-2", Role = "screen", SourceKind = "window", SourceId = "window:1", Name = "W", File = "tracks/screen-2.mp4", StartOffsetMs = 100, DurationMs = 900, StoppedAtMs = 1500 };
        Assert.Equal(1500, VideoMapper.StoppedAt(stored));
        Assert.Equal(1000, VideoMapper.StoppedAt(stored with { StoppedAtMs = null }));
        Assert.Equal(700, VideoMapper.StoppedAt(stored with { StoppedAtMs = null, EndedEarlyAtMs = 700 }));
    }
}
