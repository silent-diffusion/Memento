using System.Text.Json;
using Memento.Core.Audio;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Audio;

public sealed class PeakBuilderTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void RmsAndPeakPerFiftyMillisecondWindow()
    {
        // 1 kHz, mono: 50 samples per window.
        var builder = new PeakBuilder(1000, 1);
        var samples = new float[120];
        Array.Fill(samples, 0.5f, 0, 50);
        samples[60] = -0.75f;
        samples[110] = 0.2f;

        builder.Add(samples.AsSpan(0, 70));
        builder.Add(samples.AsSpan(70));
        builder.Complete();

        Assert.Equal(3, builder.Peaks.Count);
        Assert.Equal([0.5, 0.5], builder.Peaks[0]);
        Assert.Equal([Math.Round(0.75 / Math.Sqrt(50), 3), 0.75], builder.Peaks[1]);
        Assert.Equal([Math.Round(0.2 / Math.Sqrt(20), 3), 0.2], builder.Peaks[2]);
    }

    [Fact]
    public void WindowsSpanEveryChannel()
    {
        var builder = new PeakBuilder(1000, 2);
        var frames = new float[100];
        frames[1] = 0.6f;
        frames[2] = -0.8f;

        builder.Add(frames);
        builder.Complete();

        var pair = Assert.Single(builder.Peaks);
        Assert.Equal(0.8, pair[1]);
        Assert.Equal(Math.Round(Math.Sqrt(((0.6 * 0.6) + (0.8 * 0.8)) / 100), 3), pair[0]);
    }

    [Fact]
    public void ALoneSampleWithoutAWholeFrameIsDropped()
    {
        var builder = new PeakBuilder(1000, 2);
        builder.Add([0.5f]);
        builder.Complete();

        Assert.Empty(builder.Peaks);
    }

    [Fact]
    public async Task WritesTheMementoAudioFormat()
    {
        var builder = new PeakBuilder(1000, 1);
        var samples = new float[100];
        Array.Fill(samples, 0.123456f, 0, 50);
        builder.Add(samples);
        builder.Complete();
        var path = _directory.File("peaks.json");

        await builder.WriteAsync(path, CancellationToken.None);

        Assert.Equal("""{"schemaVersion":1,"windowMs":50,"peaks":[[0.123,0.123],[0,0]]}""", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".tmp"));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(2, document.RootElement.GetProperty("peaks").GetArrayLength());
    }

    [Fact]
    public void FourHoursOfPeaksStayCompact()
    {
        // One pair per 50 ms window: 288,000 pairs for four hours.
        Assert.Equal(288_000, 4 * 3600 * 1000 / PeakBuilder.DefaultWindowMs);
    }
}
