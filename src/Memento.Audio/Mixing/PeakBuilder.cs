using System.Text.Json;
using Memento.Audio.Codecs;
using Memento.Audio.Writing;
using NAudio.Wave;

namespace Memento.Audio.Mixing;

/// <summary>
/// Builds waveform peaks for the UI: per window (50 ms by default) the RMS and the peak magnitude over all
/// channels, in [0, 1]. Writes <c>peaks.json</c> atomically (<c>.tmp</c>, flush, move).
/// </summary>
public static class PeakBuilder
{
    public const int DefaultWindowMs = 50;

    public static Task<PeaksFile> BuildAsync(WavTrackSet track, CancellationToken cancellationToken, int windowMs = DefaultWindowMs)
    {
        ArgumentNullException.ThrowIfNull(track);
        return Task.Run(
            () =>
            {
                using var audio = MediaFoundationDecoder.Open(track);
                return Build(audio, windowMs, cancellationToken);
            },
            cancellationToken);
    }

    public static Task<PeaksFile> BuildAsync(string path, CancellationToken cancellationToken, int windowMs = DefaultWindowMs)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(
            () =>
            {
                using var audio = MediaFoundationDecoder.Open(path);
                return Build(audio, windowMs, cancellationToken);
            },
            cancellationToken);
    }

    /// <summary>Reads <paramref name="samples"/> to the end and returns its peaks.</summary>
    public static PeaksFile Build(ISampleProvider samples, int windowMs = DefaultWindowMs, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentOutOfRangeException.ThrowIfLessThan(windowMs, 1);
        var channels = samples.WaveFormat.Channels;
        var windowSamples = (int)Math.Max(1, (long)samples.WaveFormat.SampleRate * windowMs / 1000) * channels;
        var buffer = new float[windowSamples * 20];
        var peaks = new List<double[]>();
        double sum = 0;
        float peak = 0;
        var inWindow = 0;
        int n;
        while ((n = samples.Read(buffer, 0, buffer.Length)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var i = 0; i < n; i++)
            {
                var s = buffer[i];
                sum += (double)s * s;
                var a = MathF.Abs(s);
                if (a > peak)
                {
                    peak = a;
                }

                if (++inWindow == windowSamples)
                {
                    peaks.Add(Pair(sum, peak, inWindow));
                    sum = 0;
                    peak = 0;
                    inWindow = 0;
                }
            }
        }

        if (inWindow >= channels)
        {
            peaks.Add(Pair(sum, peak, inWindow));
        }

        return new PeaksFile(PeaksFile.CurrentSchemaVersion, windowMs, peaks);
    }

    /// <summary>Writes <paramref name="peaks"/> to <paramref name="path"/> via <c>path.tmp</c> and an atomic move.</summary>
    public static async Task WriteAsync(PeaksFile peaks, string path, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(peaks);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var temporary = path + ".tmp";
        await using (var file = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, FileOptions.Asynchronous))
        {
            await JsonSerializer.SerializeAsync(file, peaks, AudioJsonContext.Default.PeaksFile, cancellationToken).ConfigureAwait(false);
            await file.FlushAsync(cancellationToken).ConfigureAwait(false);
            file.Flush(true);
        }

        File.Move(temporary, path, overwrite: true);
    }

    public static async Task<PeaksFile> ReadAsync(string path, CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16, FileOptions.Asynchronous);
        return await JsonSerializer.DeserializeAsync(file, AudioJsonContext.Default.PeaksFile, cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidDataException($"{Path.GetFileName(path)} is empty.");
    }

    private static double[] Pair(double sumSquares, float peak, int count) =>
        [Math.Round(Math.Clamp(Math.Sqrt(sumSquares / count), 0, 1), 3), Math.Round(Math.Clamp(peak, 0, 1), 3)];
}
