using System.Text.Json;
using Memento.Core.Projects;
using Memento.Video.Tracks;

namespace Memento.Video.Tests.Tracks;

public sealed class VideoTrackJsonTests
{
    [Fact]
    public void AVideoTrackRoundTripsInTheManifestsShapeAndKeepsUnknownFields()
    {
        const string json = """
            {
              "id": "screen",
              "kind": "video",
              "role": "screen",
              "sourceKind": "display",
              "sourceId": "display:\\\\.\\DISPLAY1",
              "name": "Display 1",
              "file": "tracks/screen.mp4",
              "parts": [ { "file": "tracks/screen.mp4", "startOffsetMs": 0, "durationMs": 61000, "encoder": "NVIDIA H.264 Encoder MFT", "hardwareEncoder": true, "framesWritten": 1830 } ],
              "width": 2560, "height": 1440, "frameRate": 30,
              "codec": "h264", "container": "fmp4", "quality": "balanced", "bitrateBitsPerSecond": 12000000,
              "encoder": "NVIDIA H.264 Encoder MFT", "hardwareEncoder": true,
              "startOffsetMs": 0, "durationMs": 61000, "framesWritten": 1830, "framesDropped": 2,
              "futureField": { "x": 1 }
            }
            """;

        var track = JsonSerializer.Deserialize(json, VideoJsonContext.Default.VideoTrack)!;
        var again = JsonSerializer.Serialize(track, VideoJsonContext.Default.VideoTrack);

        Assert.Equal("video", track.Kind);
        Assert.Equal(@"display:\\.\DISPLAY1", track.SourceId);
        Assert.Equal(1830, track.Parts[0].FramesWritten);
        Assert.Null(track.EndReason);
        Assert.Contains("\"futureField\"", again, StringComparison.Ordinal);
        Assert.DoesNotContain("endReason", again, StringComparison.Ordinal);
        Assert.Equal(track.DurationMs, JsonSerializer.Deserialize(again, VideoJsonContext.Default.VideoTrack)!.DurationMs);
    }
}
