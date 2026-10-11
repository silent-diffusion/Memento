using Memento.Video.Hosting;
using Memento.Video.Sources;

namespace Memento.Video.Tests.Sources;

/// <summary>
/// The camera list (ENGINE-NOTES.md §R): the Media Foundation cameras and the DirectShow video input devices merged
/// into one, each camera once, with ids stable for the session and the backend the picker notes.
/// </summary>
public sealed class CameraListTests
{
    private const string ObsKey = "dshow:clsid:{A3FCE0F5-3493-419F-958A-ABA1250EC20B}";
    private const string WebcamMf = @"\\?\usb#vid_046d&pid_085e&mi_00#7&2b4c&0&0000#{e5323777-f976-4f5b-9b55-b94699c46e44}\global";
    private const string WebcamDshow = @"\\?\USB#VID_046D&PID_085E&MI_00#7&2B4C&0&0000#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global";

    [Fact]
    public void ACameraInBothListsIsKeptOnceFromMediaFoundationAndVirtualCamerasFollow()
    {
        var merged = CameraListMerger.Merge(
            [new CameraDevice("Logitech Brio", WebcamMf, WebcamMf)],
            [
                new CameraDevice("Logitech Brio", "dshow:" + WebcamDshow, WebcamDshow),
                new CameraDevice("OBS Virtual Camera", ObsKey, null),
                new CameraDevice("NDI Webcam Video 1", "dshow:clsid:{90F3B6A6-0000-4000-8000-000000000001}", null),
            ]);

        Assert.Equal(["Logitech Brio", "OBS Virtual Camera", "NDI Webcam Video 1"], merged.Select(c => c.Name));
        Assert.Equal([CameraBackend.MediaFoundation, CameraBackend.DirectShow, CameraBackend.DirectShow], merged.Select(c => c.Backend!.Value));
        Assert.Equal("camera:" + WebcamMf, merged[0].Id.ToString());
        Assert.Equal("camera:" + ObsKey, merged[1].Id.ToString());
        Assert.All(merged, c => Assert.Equal("Camera", c.Detail));
    }

    [Fact]
    public void ADirectShowEntryWithoutAPathIsADuplicateWhenMediaFoundationHasItsName()
    {
        var merged = CameraListMerger.Merge(
            [new CameraDevice("Integrated Camera", @"\\?\usb#vid_1#a#{e5323777-f976-4f5b-9b55-b94699c46e44}\global", @"\\?\usb#vid_1#a#{e5323777-f976-4f5b-9b55-b94699c46e44}\global")],
            [new CameraDevice("integrated camera ", "dshow:name:integrated camera", null), new CameraDevice("Capture card", @"dshow:\\?\pci#ven_2#b#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global", @"\\?\pci#ven_2#b#{65e8773d-8f56-11d0-a3b9-00a0c9223196}\global")]);

        Assert.Equal(["Integrated Camera", "Capture card"], merged.Select(c => c.Name));
        Assert.Equal(CameraBackend.DirectShow, merged[1].Backend);
    }

    [Fact]
    public void WithoutMediaFoundationCamerasEveryDirectShowDeviceIsListed()
    {
        var merged = CameraListMerger.Merge([], [new CameraDevice("OBS Virtual Camera", ObsKey, null)]);

        var camera = Assert.Single(merged);
        Assert.Equal(VideoSourceKind.Camera, camera.Id.Kind);
        Assert.Equal(CameraBackend.DirectShow, camera.Backend);
        Assert.Equal("dshow", MediaFoundationVideoCapture.BackendName(camera.Backend));
        Assert.Equal("mf", MediaFoundationVideoCapture.BackendName(CameraBackend.MediaFoundation));
        Assert.Null(MediaFoundationVideoCapture.BackendName(VideoSourceId.Display(@"\\.\DISPLAY1").Backend));
    }

    [Fact]
    public void DirectShowKeysAreThePathTheClsidOrTheNameAndRepeatsAreCounted()
    {
        var seen = new Dictionary<string, int>(StringComparer.Ordinal);

        Assert.Equal("dshow:" + WebcamDshow, CameraListMerger.DirectShowKey("Brio", WebcamDshow, "{17CCA71B-ECD7-11D0-B908-00A0C9223196}", seen));
        Assert.Equal(ObsKey, CameraListMerger.DirectShowKey("OBS Virtual Camera", null, "{a3fce0f5-3493-419f-958a-aba1250ec20b}", seen));
        Assert.Equal("dshow:name:Old capture", CameraListMerger.DirectShowKey(" Old capture ", " ", null, seen));
        Assert.Equal("dshow:name:Old capture#2", CameraListMerger.DirectShowKey("Old capture", null, "not a guid", seen));
        Assert.Equal(ObsKey + "#2", CameraListMerger.DirectShowKey("OBS Virtual Camera", null, "{A3FCE0F5-3493-419F-958A-ABA1250EC20B}", seen));
    }

    [Theory]
    [InlineData(WebcamMf, @"\\?\usb#vid_046d&pid_085e&mi_00#7&2b4c&0&0000")]
    [InlineData(WebcamDshow, @"\\?\usb#vid_046d&pid_085e&mi_00#7&2b4c&0&0000")]
    [InlineData(@"\\?\swd#vcamdevapi#abc", @"\\?\swd#vcamdevapi#abc")]
    [InlineData("usb-test-camera", null)]
    [InlineData(null, null)]
    public void TheDeviceInstanceLeavesTheInterfaceClassOut(string? path, string? expected) => Assert.Equal(expected, CameraListMerger.Instance(path));

    [Fact]
    public void ADirectShowCameraIdRoundTripsAndSaysItsBackend()
    {
        Assert.True(VideoSourceId.TryParse("camera:" + ObsKey, out var id));

        Assert.Equal(VideoSourceKind.Camera, id.Kind);
        Assert.Equal(ObsKey, id.Key);
        Assert.Equal(CameraBackend.DirectShow, id.Backend);
        Assert.Equal("camera:" + ObsKey, id.ToString());
        Assert.Equal(CameraBackend.MediaFoundation, VideoSourceId.Camera(WebcamMf).Backend);
        Assert.Null(VideoSourceId.Window(new IntPtr(0x10)).Backend);
    }
}
