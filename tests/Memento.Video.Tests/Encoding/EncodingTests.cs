using Memento.Video.Encoding;
using Memento.Video.Gpu;
using Memento.Video.Recording;

namespace Memento.Video.Tests.Encoding;

public sealed class EncodingTests
{
    [Fact]
    public void BalancedIsAbout12MegabitsAt1440p30AsSettingsSays()
    {
        Assert.Equal(12_000_000, VideoBitrate.For(2560, 1440, 30, VideoQuality.Balanced));
        Assert.Equal(6_000_000, VideoBitrate.For(2560, 1440, 30, VideoQuality.Smaller));
        Assert.Equal(20_000_000, VideoBitrate.For(2560, 1440, 30, VideoQuality.Best));
    }

    [Fact]
    public void BitrateGrowsWithSizeAndFrameRateButStaysWithinBounds()
    {
        var p720 = VideoBitrate.For(1280, 720, 30, VideoQuality.Balanced);
        var p1080 = VideoBitrate.For(1920, 1080, 30, VideoQuality.Balanced);
        var p1080x60 = VideoBitrate.For(1920, 1080, 60, VideoQuality.Balanced);

        Assert.True(p720 < p1080 && p1080 < p1080x60);
        Assert.True(p1080x60 < 2 * p1080);
        Assert.Equal(500_000, VideoBitrate.For(64, 64, 24, VideoQuality.Smaller));
        Assert.Equal(5_400_000_000, VideoBitrate.BytesPerHour(12_000_000));
    }

    [Theory]
    [InlineData(1921, 1081, 1920, 1080)]
    [InlineData(5120, 1440, 4096, 1152)]
    [InlineData(10, 10, 64, 64)]
    public void CaptureSizesAreMadeEncodable(int width, int height, int w, int h) =>
        Assert.Equal((w, h), VideoEncoderSettings.EncodableSize(width, height));

    [Fact]
    public void AWindowOfAnotherShapeIsLetterboxedIntoTheTrack()
    {
        var tall = FrameConverter.Fit(1080, 1920, 1920, 1080);
        var same = FrameConverter.Fit(2560, 1440, 1920, 1080);

        Assert.Equal((656, 0, 1264, 1080), (tall.Left, tall.Top, tall.Right, tall.Bottom));
        Assert.Equal((0, 0, 1920, 1080), (same.Left, same.Top, same.Right, same.Bottom));
    }

    [Fact]
    public void FrameDurationFollowsTheFrameRate()
    {
        Assert.Equal(333_333, new VideoEncoderSettings(1280, 720, 30).FrameDurationTicks);
        Assert.Equal(166_666, new VideoEncoderSettings(1280, 720, 60).FrameDurationTicks);
    }

    [Fact]
    public void PartsAreNamedLikeTheAudioRollover()
    {
        var options = new VideoSessionOptions("screen", "screen", "p", new VideoEncoderSettings(1280, 720), 0);

        Assert.Equal("tracks/screen.mp4", options.RelativePartPath(1));
        Assert.Equal("tracks/screen.part2.mp4", options.RelativePartPath(2));
    }

    [Fact]
    public void ErrorSentencesNameTheThingTheTimeWhatIsSafeAndTheFix()
    {
        Assert.Equal("Display 2 disconnected at 23:40. Video is paused; audio and the camera keep recording. Switch the screen track to another display, or keep waiting for it to come back.", VideoErrors.DisplayDisconnected("Display 2", 1_420_000));
        Assert.StartsWith("Hardware encoder failed at 12:03. Switched to software encoding", VideoErrors.HardwareEncoderFailed(723_000), StringComparison.Ordinal);
        Assert.Contains("audio keeps recording", VideoErrors.DiskFull("screen", 60_000), StringComparison.Ordinal);
        Assert.Contains("Privacy & security › Screen capture", VideoErrors.ScreenCaptureBlocked(), StringComparison.Ordinal);
        Assert.Contains("rather than recorded as black", VideoErrors.ProtectedWindow("Player"), StringComparison.Ordinal);
    }
}
