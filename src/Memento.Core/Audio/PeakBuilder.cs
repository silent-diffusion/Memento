using System.Text.Json;

namespace Memento.Core.Audio;

/// <summary>
/// Waveform peaks for the UI: for every window of about 50 ms, the minimum and the maximum sample over all
/// channels, appended as two values to one flat list. Written as
/// <c>{ "schemaVersion": 1, "windowMs": 50, "peaks": [min0, max0, min1, max1, …] }</c>.
/// Memory is 8 bytes per window (about 2.3 MB for four hours).
/// </summary>
public sealed class PeakBuilder
{
    public const int SchemaVersion = 1;
    public const int DefaultWindowMs = 50;

    private readonly int _channels;
    private readonly int _windowFrames;
    private readonly List<float> _peaks = [];
    private int _framesInWindow;
    private float _min;
    private float _max;

    public PeakBuilder(int sampleRate, int channels, int windowMs = DefaultWindowMs)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(sampleRate, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(channels, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(windowMs, 1);
        _channels = channels;
        WindowMs = windowMs;
        _windowFrames = Math.Max(1, (int)((long)sampleRate * windowMs / 1000));
        ResetWindow();
    }

    public int WindowMs { get; }

    /// <summary>Min/max pairs so far (complete windows only until <see cref="Complete"/>).</summary>
    public IReadOnlyList<float> Peaks => _peaks;

    /// <summary>Adds interleaved frames.</summary>
    public void Add(ReadOnlySpan<float> interleaved)
    {
        var frames = interleaved.Length / _channels;
        for (var f = 0; f < frames; f++)
        {
            var o = f * _channels;
            for (var c = 0; c < _channels; c++)
            {
                var s = interleaved[o + c];
                if (s < _min)
                {
                    _min = s;
                }

                if (s > _max)
                {
                    _max = s;
                }
            }

            if (++_framesInWindow == _windowFrames)
            {
                CloseWindow();
            }
        }
    }

    /// <summary>Closes a final partial window.</summary>
    public void Complete()
    {
        if (_framesInWindow > 0)
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
                    // Four decimals is far below one pixel of any waveform and keeps the file small.
                    writer.WriteNumberValue(Math.Round((double)_peaks[i], 4));
                    if ((i & 0x3FFF) == 0)
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
        _peaks.Add(_min);
        _peaks.Add(_max);
        ResetWindow();
    }

    private void ResetWindow()
    {
        _framesInWindow = 0;
        _min = 0f;
        _max = 0f;
    }
}
