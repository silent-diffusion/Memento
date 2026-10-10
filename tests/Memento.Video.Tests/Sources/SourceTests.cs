using Memento.Video.Sources;

namespace Memento.Video.Tests.Sources;

public sealed class SourceTests
{
    [Theory]
    [InlineData(@"display:\\.\DISPLAY1", VideoSourceKind.Display, @"\\.\DISPLAY1")]
    [InlineData("window:1A2B", VideoSourceKind.Window, "1A2B")]
    [InlineData(@"camera:\\?\usb#vid_046d&pid_085e#{e5323777}", VideoSourceKind.Camera, @"\\?\usb#vid_046d&pid_085e#{e5323777}")]
    public void IdsRoundTripThroughText(string text, VideoSourceKind kind, string key)
    {
        Assert.True(VideoSourceId.TryParse(text, out var id));
        Assert.Equal(kind, id.Kind);
        Assert.Equal(key, id.Key);
        Assert.Equal(text, id.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("display:")]
    [InlineData("screen:1")]
    [InlineData("window:not-hex")]
    public void BadIdsAreRefused(string? text) => Assert.False(VideoSourceId.TryParse(text, out _));

    [Fact]
    public void AWindowIdKeepsItsHandle()
    {
        var id = VideoSourceId.Window(new IntPtr(0x40A2C));

        Assert.Equal(new IntPtr(0x40A2C), id.WindowHandle);
        Assert.Equal(IntPtr.Zero, VideoSourceId.Display("x").WindowHandle);
    }

    [Fact]
    public void ThePrimaryDisplayIsDisplay1AndTheOthersAreNamedByWhereTheyAre()
    {
        var displays = DisplayNaming.Name(
        [
            new DisplayNaming.DisplayEntry(@"\\.\DISPLAY4", 2560, 0, 1920, 1080, false, 144),
            new DisplayNaming.DisplayEntry(@"\\.\DISPLAY1", 0, 0, 2560, 1440, true, 60),
            new DisplayNaming.DisplayEntry(@"\\.\DISPLAY2", -1080, -400, 1080, 1920, false, 60),
        ]);

        Assert.Equal(["Display 1", "Display 2", "Display 3"], displays.Select(d => d.Name));
        Assert.Equal(@"\\.\DISPLAY1", displays[0].Id.Key);
        Assert.Equal("2560 \u00d7 1440 \u00b7 60 Hz \u00b7 primary", displays[0].Detail);
        Assert.Equal("1080 \u00d7 1920 \u00b7 60 Hz \u00b7 left of Display 1", displays[1].Detail);
        Assert.Equal("1920 \u00d7 1080 \u00b7 144 Hz \u00b7 right of Display 1", displays[2].Detail);
        Assert.True(displays[0].IsPrimary);
    }

    [Theory]
    [InlineData(null, null, null, CameraAccess.Unknown)]
    [InlineData("Allow", "Allow", "Allow", CameraAccess.Allowed)]
    [InlineData("Deny", "Allow", "Allow", CameraAccess.DeniedForPc)]
    [InlineData("Allow", "Deny", "Allow", CameraAccess.DeniedForUser)]
    [InlineData("Allow", "Allow", "Deny", CameraAccess.DeniedForDesktopApps)]
    [InlineData(null, null, "Deny", CameraAccess.DeniedForDesktopApps)]
    public void ThePrivacySwitchThatIsOffIsNamed(string? machine, string? user, string? desktop, CameraAccess expected) =>
        Assert.Equal(expected, CameraPrivacy.Read(machine, user, desktop));

    [Fact]
    public void TheAppUsingTheCameraIsTheOneThatHasNotStopped()
    {
        var entries = new List<(string, long, long)>
        {
            ("C:#Program Files#Example#Meet.exe", 500, 600),
            ("C:#Tools#Memento#Memento.exe", 900, 0),
            ("Contoso.CallApp_8wekyb3d8bbwe", 700, 0),
        };

        Assert.Equal("Contoso.CallApp", CameraUsage.FindActive(entries, @"C:\Tools\Memento\Memento.exe"));
        Assert.Null(CameraUsage.FindActive([("C:#a#b.exe", 5, 6)], null));
    }

    [Fact]
    public void ADesktopAppIsNamedByItsFileWhenItsDescriptionIsUnknown() =>
        Assert.Equal("Meet", CameraUsage.DisplayName("Z:#Not#There#Meet.exe"));

    [Fact]
    public void ThePrivacyDenialSaysWhichSwitchToTurnOn()
    {
        var ex = CameraErrors.Classify(CameraErrors.AccessDenied, "Front camera", CameraAccess.DeniedForDesktopApps, null);

        Assert.Equal(VideoErrorCode.CameraPrivacyDenied, ex.Code);
        Assert.Equal("Front camera is blocked by Windows privacy settings. Recording continues with the screen and all audio tracks. Turn on Privacy & security › Camera › Let desktop apps access your camera, then retry the camera.", ex.Message);
    }

    [Theory]
    [InlineData(CameraErrors.Busy)]
    [InlineData(CameraErrors.FailedStartStreaming)]
    [InlineData(CameraErrors.DevicePreempted)]
    public void ACameraHeldByAnotherAppNamesThatApp(int hresult)
    {
        var ex = CameraErrors.Classify(hresult, "Front camera", CameraAccess.Allowed, "Contoso Meet");

        Assert.Equal(VideoErrorCode.CameraInUse, ex.Code);
        Assert.StartsWith("Front camera is in use by Contoso Meet. Recording continues", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ACameraHeldByAnUnknownAppStillSaysSo() =>
        Assert.StartsWith("Front camera is in use by another app.", CameraErrors.Classify(CameraErrors.Busy, "Front camera", CameraAccess.Allowed, null).Message, StringComparison.Ordinal);

    [Fact]
    public void AnUnpluggedCameraEndsItsTrackAtTheTime()
    {
        var ex = CameraErrors.Classify(CameraErrors.DeviceInvalidated, "Front camera", CameraAccess.Allowed, null, atMs: 125_000);

        Assert.Equal(VideoErrorCode.SourceLost, ex.Code);
        Assert.StartsWith("Front camera was disconnected at 2:05.", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLargestFastUncompressedFormatUpTo1080pIsChosen()
    {
        CameraFormatChooser.Format[] formats =
        [
            new(0, 3840, 2160, 30, true),
            new(1, 1920, 1080, 30, true),
            new(2, 1920, 1080, 5, false),
            new(3, 1280, 720, 30, false),
            new(4, 1920, 1080, 30, false),
        ];

        Assert.Equal(4, CameraFormatChooser.Choose(formats)!.Index);
        Assert.Equal(1, CameraFormatChooser.Choose(formats[..4])!.Index);
        Assert.Equal(0, CameraFormatChooser.Choose([formats[0]])!.Index);
        Assert.Null(CameraFormatChooser.Choose([]));
    }
}
