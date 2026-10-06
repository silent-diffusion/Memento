using NAudio.Wave;

namespace Memento.Audio.Codecs;

/// <summary>Passes reads through, honouring cancellation and reporting progress (0..1) by bytes.</summary>
internal sealed class CancellableWaveProvider(IWaveProvider inner, long totalBytes, IProgress<double>? progress, CancellationToken cancellationToken) : IWaveProvider
{
    private long _read;
    private double _lastReported = -1;

    public WaveFormat WaveFormat => inner.WaveFormat;

    public long BytesRead => _read;

    public int Read(byte[] buffer, int offset, int count)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var n = inner.Read(buffer, offset, count);
        _read += n;
        if (progress is not null && totalBytes > 0)
        {
            var fraction = Math.Min(1.0, _read / (double)totalBytes);
            if (fraction - _lastReported >= 0.01 || (n == 0 && _lastReported < 1))
            {
                _lastReported = n == 0 ? 1 : fraction;
                progress.Report(_lastReported);
            }
        }

        return n;
    }
}
