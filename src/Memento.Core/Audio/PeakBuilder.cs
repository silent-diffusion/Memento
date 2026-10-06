using System.Text.Json;

namespace Memento.Core.Audio;

/// <summary>
/// Waveform peaks for the UI, in the same format as Memento.Audio's peak builder: for every window of about 50 ms,
/// the RMS and the peak magnitude over all channels, both in [0, 1] and rounded to three decimals. Written as
/// <c>{ "schemaVersion": 1, "windowMs": 50, "peaks": [[rms, peak], …] }</c>. Memory is one pair per window
/// (about 288,000 pairs for four hours).
/// </summary>
public sealed class PeakBuilder
{
    public const int SchemaVersion = 1;
    public const int DefaultWindowMs = 50;

    private readonly int _channels;
    private readonly int _windowSamples;
    private readonly List<double[]> _peaks = [];
    private int _inWindow;
    private double _sumSquares;
    private float _peak;

    public PeakBuilder(int sampleRate, int channels, int windowMs = DefaultWindowMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(windowMs, 1);
        _channels = channels;
        WindowMs = windowMs;
        _windowSamples = (int)Math.Max(1, (long)sampleRate * windowMs / 1000) * channels;
    }

    public int WindowMs { get; }

    /// <summary>[rms, peak] per window so far (complete windows only until <see cref="Complete"/>).</summary>
    public IReadOnlyList<double[]> Peaks => _peaks;

    /// <summary>Adds interleaved samples.</summary>
    public void Add(ReadOnlySpan<float> interleaved)
    {
        foreach (var sample in interleaved)
        {
            _sumSquares += (double)sample * sample;
            var magnitude = MathF.Abs(sample);
            if (magnitude > _peak)
            {
                _peak = magnitude;
            }

            if (++_inWindow == _windowSamples)
            {
                CloseWindow();
            }
        }
    }

    /// <summary>Closes a final partial window of at least one frame.</summary>
    public void Complete()
    {
        if (_inWindow >= _channels)
        {
            CloseWindow();
        }
    }

    /// <summary>Writes the peaks file atomically (<c>.tmp</c>, flush, move).</summary>
    public async Task WriteAsync(string path, CancellationToken cancellationToken)
    {
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 64 * 1024, useAsync: true))
        {
            await using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                writer.WriteNumber("schemaVersion", SchemaVersion);
                writer.WriteNumber("windowMs", WindowMs);
                writer.WriteStartArray("peaks");
                for (var i = 0; i < _peaks.Count; i++)
                {
                    writer.WriteStartArray();
                    writer.WriteNumberValue(_peaks[i][0]);
                    writer.WriteNumberValue(_peaks[i][1]);
                    writer.WriteEndArray();
                    if ((i & 0x1FFF) == 0)
                    {
                        await writer.FlushAsync(cancellationToken);
                    }
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            await stream.FlushAsync(cancellationToken);
            stream.Flush(flushToDisk: true);
        }

        File.Move(temporary, path, overwrite: true);
    }

    private void CloseWindow()
    {
        var rms = Math.Sqrt(_sumSquares / _inWindow);
        _peaks.Add([Math.Round(Math.Clamp(rms, 0, 1), 3), Math.Round(Math.Clamp(_peak, 0, 1), 3)]);
        _sumSquares = 0;
        _peak = 0;
        _inWindow = 0;
    }
}
