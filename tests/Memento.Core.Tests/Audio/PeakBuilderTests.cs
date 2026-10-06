using System.Text.Json;
using Memento.Core.Audio;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Audio;

public sealed class PeakBuilderTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void TwoValuesPerFiftyMillisecondWindow()
    {
        // 1 kHz, mono: 50 frames per window.
        var builder = new PeakBuilder(1000, 1);
        var samples = new float[120];
        samples[10] = 0.5f;
        samples[20] = -0.25f;
        samples[60] = 0.75f;
        samples[110] = -1f;

        builder.Add(samples.AsSpan(0, 70));
        builder.Add(samples.AsSpan(70));
        builder.Complete();

        Assert.Equal([-0.25f, 0.5f, 0f, 0.75f, -1f, 0f], builder.Peaks);
    }

    [Fact]
    public void PeaksSpanEveryChannel()
    {
        var builder = new PeakBuilder(1000, 2);
        var frames = new float[100];
        frames[1] = 0.6f;
        frames[2] = -0.4f;

        builder.Add(frames);
        builder.Complete();

        Assert.Equal([-0.4f, 0.6f], builder.Peaks);
    }

    [Fact]
    public async Task WritesTheDocumentedFile()
    {
        var builder = new PeakBuilder(1000, 1);
        var samples = new float[100];
        samples[0] = 0.123456f;
        samples[50] = -0.5f;
        builder.Add(samples);
        builder.Complete();
        var path = _directory.File("peaks.json");

        await builder.WriteAsync(path, CancellationToken.None);

        Assert.Equal("""{"schemaVersion":1,"windowMs":50,"peaks":[0,0.1235,-0.5,0]}""", await File.ReadAllTextAsync(path));
        Assert.False(File.Exists(path + ".tmp"));
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        Assert.Equal(4, document.RootElement.GetProperty("peaks").GetArrayLength());
    }

    [Fact]
    public void FourHoursOfPeaksStayCompact()
    {
        var windows = 4 * 3600 * 1000 / PeakBuilder.DefaultWindowMs;

        // Two floats per window: about 2.3 MB in memory for four hours.
        Assert.Equal(288_000, windows);
        Assert.True(windows * 2 * sizeof(float) < 2.5 * 1024 * 1024);
    }
}
