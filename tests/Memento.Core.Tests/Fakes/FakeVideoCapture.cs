using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Projects;
using Memento.Core.Video;

namespace Memento.Core.Tests.Fakes;

/// <summary>
/// Video capture without a graphics card: each started track is a <see cref="FakeVideoTrackRecorder"/> that writes a small
/// file into <c>tracks/</c> and records what the coordinator asked of it. A source id in <see cref="Refuse"/> fails to start.
/// </summary>
internal sealed class FakeVideoCapture : IVideoCapture
{
    public const string Display1 = @"display:\\.\DISPLAY1";
    public const string Display2 = @"display:\\.\DISPLAY2";
    public const string Window = "window:1A2B3C";
    public const string Camera = "camera:usb-test-camera";
    public const string VirtualCamera = "camera:dshow:clsid:{A3FCE0F5-3493-419F-958A-ABA1250EC20B}";

    private readonly object _gate = new();

    public List<FakeVideoTrackRecorder> Started { get; } = [];

    /// <summary>Source id → the error its start throws.</summary>
    public Dictionary<string, VideoStartException> Refuse { get; } = new(StringComparer.Ordinal);

    public bool IsAvailable { get; set; } = true;

    /// <summary>Windows that opened since (listed after the first one).</summary>
    public List<VideoSource> MoreWindows { get; } = [];

    public VideoSourcesResult ListSources() => new(
        [
            new VideoSource(Display1, "display", "Display 1", "1920 × 1080 · 60 Hz · primary", 1920, 1080, 60, true, false, false, null, null),
            new VideoSource(Display2, "display", "Display 2", "1920 × 1080 · 60 Hz · right of Display 1", 1920, 1080, 60, false, false, false, null, null),
        ],
        [new VideoSource(Window, "window", "Planning notes", "Notes · 1280 × 720", 1280, 720, 0, false, false, false, "Notes", null), .. MoreWindows],
        [
            new VideoSource(Camera, "camera", "Test camera", "Camera", 0, 0, 0, false, false, false, null, "mf"),
            new VideoSource(VirtualCamera, "camera", "OBS Virtual Camera", "Camera", 0, 0, 0, false, false, false, null, "dshow"),
        ]);

    public Task<VideoThumbnailResult> ThumbnailAsync(string sourceId, int width, CancellationToken cancellationToken) =>
        Refuse.TryGetValue(sourceId, out var refused)
            ? Task.FromException<VideoThumbnailResult>(refused)
            : Task.FromResult(new VideoThumbnailResult("data:image/jpeg;base64,AAAA", width, width * 9 / 16));

    public Task<IVideoTrackRecorder> StartTrackAsync(VideoTrackRequest request, CancellationToken cancellationToken)
    {
        if (Refuse.TryGetValue(request.SourceId, out var refused))
        {
            return Task.FromException<IVideoTrackRecorder>(refused);
        }

        var recorder = new FakeVideoTrackRecorder(request, NameOf(request.SourceId));
        lock (_gate)
        {
            Started.Add(recorder);
        }

        return Task.FromResult<IVideoTrackRecorder>(recorder);
    }

    public static string NameOf(string sourceId) => sourceId switch
    {
        Display1 => "Display 1",
        Display2 => "Display 2",
        Window => "Planning notes",
        _ => "Test camera",
    };

    public static VideoStartException CameraInUse() =>
        new(DomainErrorCodes.VideoCameraInUse, "Test camera is in use by Zoom. Recording continues with the screen and all audio tracks.", "Zoom");
}
