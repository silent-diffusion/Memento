using Memento.Core.Audio;
using Memento.Core.Recording;
using Memento.Core.Tests.Audio;
using Memento.Core.Tests.Fakes;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Recording;

public sealed class TrackFinalizerTests : IDisposable
{
    private readonly TempDirectory _directory = new();

    public TrackFinalizerTests()
    {
        Directory.CreateDirectory(_directory.File("tracks"));
        WavTestFiles.Write(_directory.File("tracks/mic.wav"), PcmFormat.Pcm16(48_000, 1), 48_000, (_, _) => 0.2f);
        WavTestFiles.Write(_directory.File("tracks/system.wav"), PcmFormat.Pcm16(48_000, 2), 24_000, (_, c) => c == 0 ? 0.1f : 0.3f);
    }

    public void Dispose() => _directory.Dispose();

    private static FinalizeRequest Request(string folder, StorageFormat storage) =>
        new(folder, [new FinalizeTrackInput("mic", "tracks/mic.wav", 0), new FinalizeTrackInput("system", "tracks/system.wav", 500)], storage);

    [Fact]
    public async Task EncodesWithTheConfiguredCodecAndHashesEveryFile()
    {
        var flac = new FakeEncoder("flac", ".flac");
        var finalizer = new TrackFinalizer([new PassThroughWavEncoder(), flac], TimeProvider.System, NullLogger<TrackFinalizer>.Instance);
        var progress = new List<int>();

        var result = await finalizer.FinalizeAsync(Request(_directory.Path, new StorageFormat("flac", null, false)), new SyncProgress(progress), CancellationToken.None);

        Assert.Equal("flac", result.Codec);
        Assert.Equal(["tracks/mic.flac", "tracks/system.flac"], result.Tracks.Select(t => t.File));
        Assert.Equal("mix.flac", result.Mix.File);
        Assert.Equal(["tracks/mic.wav", "tracks/system.wav"], result.ObsoleteCaptureFiles);
        Assert.Equal(1000, result.Tracks[0].DurationMs);
        Assert.Equal(500, result.Tracks[1].DurationMs);
        Assert.Equal(1000, result.Mix.DurationMs);
        Assert.Equal(3, flac.Calls.Count);
        Assert.Equal(await FileHashes.Sha256Async(_directory.File("tracks/mic.flac"), CancellationToken.None), result.Tracks[0].Sha256);
        Assert.Equal(await FileHashes.Sha256Async(_directory.File("mix.flac"), CancellationToken.None), result.Mix.Sha256);
        Assert.False(File.Exists(_directory.File("mix.tmp.wav")));
        Assert.True(File.Exists(_directory.File("tracks/mic.wav")));
        Assert.Empty(result.Notes);
        Assert.Equal(100, progress[^1]);
    }

    [Fact]
    public async Task FallsBackToWavWhenTheCodecIsNotInstalled()
    {
        var finalizer = new TrackFinalizer([new PassThroughWavEncoder()], TimeProvider.System, NullLogger<TrackFinalizer>.Instance);

        var result = await finalizer.FinalizeAsync(Request(_directory.Path, new StorageFormat("mp3", 192, false)), null, CancellationToken.None);

        Assert.Equal("wav", result.Codec);
        Assert.Equal("tracks/mic.wav", result.Tracks[0].File);
        Assert.Empty(result.ObsoleteCaptureFiles);
        Assert.Equal("Kept as WAV: this build has no MP3 encoder. Nothing was lost; the files are larger.", Assert.Single(result.Notes));
    }

    [Fact]
    public async Task LossyEncodersGetTheBitrateAndDownmix()
    {
        var mp3 = new FakeEncoder("mp3", ".mp3", lossless: false);
        var finalizer = new TrackFinalizer([mp3], TimeProvider.System, NullLogger<TrackFinalizer>.Instance);

        var result = await finalizer.FinalizeAsync(Request(_directory.Path, new StorageFormat("mp3", 128, DownmixMono: true)), null, CancellationToken.None);

        Assert.All(mp3.Calls, c => Assert.Equal(128, c.Options.BitrateKbps));
        Assert.True(mp3.Calls.Single(c => c.Source.EndsWith("system.wav", StringComparison.Ordinal)).Options.DownmixMono);
        Assert.Equal(1, result.Mix.Channels);
        Assert.Equal(1, result.Tracks[1].Channels);
    }

    [Fact]
    public async Task WavDownmixWritesMonoTracks()
    {
        var finalizer = new TrackFinalizer([new PassThroughWavEncoder()], TimeProvider.System, NullLogger<TrackFinalizer>.Instance);

        var result = await finalizer.FinalizeAsync(Request(_directory.Path, new StorageFormat("wav", null, DownmixMono: true)), null, CancellationToken.None);

        var system = WavInfo.Read(_directory.File("tracks/system.wav"));
        Assert.Equal(1, system.Format.Channels);
        Assert.Equal(0.2f, WavTestFiles.ReadAll(_directory.File("tracks/system.wav"))[10], 3);
        Assert.Equal(500, result.Tracks[1].DurationMs);
    }

    [Fact]
    public async Task RunningTwiceAfterAnInterruptionWorks()
    {
        var finalizer = new TrackFinalizer([new PassThroughWavEncoder()], TimeProvider.System, NullLogger<TrackFinalizer>.Instance);
        var request = Request(_directory.Path, new StorageFormat("flac", null, false));

        var first = await finalizer.FinalizeAsync(request, null, CancellationToken.None);
        var second = await finalizer.FinalizeAsync(request, null, CancellationToken.None);

        Assert.Equal(first.Mix.Sha256, second.Mix.Sha256);
        Assert.Equal(first.Tracks[0].Sha256, second.Tracks[0].Sha256);
    }

    private sealed class FakeEncoder(string codec, string extension, bool lossless = true) : IAudioEncoder
    {
        public List<(string Source, string Destination, AudioEncodeOptions Options)> Calls { get; } = [];

        public string Codec => codec;

        public string FileExtension => extension;

        public bool IsLossless => lossless;

        public async Task EncodeAsync(string sourceWavPath, string destinationPath, AudioEncodeOptions options, CancellationToken cancellationToken)
        {
            Calls.Add((sourceWavPath, destinationPath, options));
            var source = await File.ReadAllBytesAsync(sourceWavPath, cancellationToken);
            await File.WriteAllBytesAsync(destinationPath, [(byte)'f', (byte)'L', .. source], cancellationToken);
        }
    }

    private sealed class SyncProgress(List<int> values) : IProgress<int>
    {
        public void Report(int value) => values.Add(value);
    }
}
