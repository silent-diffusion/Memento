using Memento.Audio.Recording;
using Memento.Audio.Sources;
using Memento.Audio.Writing;

namespace Memento.Audio.Tests.Recording;

public sealed class SessionTimelineTests : IDisposable
{
    private const long Ms = QpcClock.TicksPerMillisecond;
    private const long Start = 1_000_000 * Ms;
    private readonly TempDirectory _dir = new();

    public void Dispose() => _dir.Dispose();

    [Fact]
    public void TimelineExcludesPauses()
    {
        var timeline = new SessionTimeline(Start);
        Assert.Equal(TimeSpan.FromMilliseconds(500), timeline.ToTimeline(Start + (500 * Ms)));

        Assert.True(timeline.Pause(Start + (1_000 * Ms)));
        Assert.False(timeline.Pause(Start + (1_100 * Ms)));
        Assert.Equal(TimeSpan.FromMilliseconds(1_000), timeline.ToTimeline(Start + (1_500 * Ms)));
        var gap = timeline.Resume(Start + (3_000 * Ms));
        Assert.Equal((TimeSpan.FromMilliseconds(1_000), TimeSpan.FromMilliseconds(2_000)), gap);
        Assert.Equal(TimeSpan.FromMilliseconds(1_500), timeline.ToTimeline(Start + (3_500 * Ms)));
        Assert.Null(timeline.Resume(Start + (4_000 * Ms)));
        Assert.Equal(TimeSpan.Zero, timeline.ToTimeline(Start - (100 * Ms)));
    }

    [Fact]
    public void PaddingStartsATrackAtTheSessionStartAndRespectsPauses()
    {
        var now = Start;
        TrackWriter writer;
        using (writer = new TrackWriter(new TrackWriterOptions(_dir.Path, "mic", AudioFormat.Float32Stereo48k) { DurableCheckpoints = false, Clock = () => now }))
        {
            writer.SetStart(Start);
            writer.PadStartFrom(Start);
            writer.Write(Signals.AsBytes(Signals.Constant(480, 2, 0.5f)), Start + (250 * Ms)); // 250 ms start-up latency
        }

        Assert.Equal(Start, writer.FirstFrameQpc);
        Assert.Equal((250 + 10) * 48, writer.FramesWritten);
        using var reader = WavTrackSet.Open(_dir.Path, "mic").OpenReader();
        var bytes = new byte[writer.FramesWritten * 6];
        reader.Read(bytes);
        Assert.Equal(0, PcmConverter.ReadInt24(bytes.AsSpan(((250 * 48) - 1) * 6)));
        Assert.Equal(4_194_304, PcmConverter.ReadInt24(bytes.AsSpan(250 * 48 * 6)));
    }

    [Theory]
    [InlineData("Zoom", "zoom")]
    [InlineData("Microsoft Teams (work)", "microsoft-teams-work")]
    [InlineData("名前", "app")]
    public void AppStemsAreSafeFileNames(string processName, string expected)
    {
        Assert.Equal(expected, TrackNaming.Sanitize(processName));
    }

    [Fact]
    public void StemsAreUniqueAgainstTheSessionAndTheFolder()
    {
        File.WriteAllBytes(_dir.File("mic.wav"), []);
        var info = new AudioSourceInfo("app:7", AudioSourceKind.Application, "Player", "Only this app", false, 7) { ProcessName = "Player" };

        Assert.Equal("mic-2", TrackNaming.UniqueStem("mic", _dir.Path, []));
        Assert.Equal("system-2", TrackNaming.UniqueStem("system", _dir.Path, ["system"]));
        Assert.Equal("app-player", TrackNaming.BaseStem(AudioSourceId.Application(7), info));
        Assert.Equal("app-7", TrackNaming.BaseStem(AudioSourceId.Application(7), null));
    }
}
