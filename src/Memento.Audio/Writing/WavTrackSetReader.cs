using NAudio.Wave;

namespace Memento.Audio.Writing;

/// <summary>Reads the data chunks of a <see cref="WavTrackSet"/>'s parts back to back as one PCM stream.</summary>
public sealed class WavTrackSetReader : IWaveProvider, IDisposable
{
    private readonly IReadOnlyList<WavFileInfo> _parts;
    private int _index = -1;
    private FileStream? _current;
    private long _remainingInPart;

    internal WavTrackSetReader(WavTrackSet set)
    {
        _parts = set.Parts;
        Format = set.Format;
        WaveFormat = set.Format.ToWaveFormat();
        TotalBytes = set.TotalDataBytes;
    }

    public AudioFormat Format { get; }

    public WaveFormat WaveFormat { get; }

    public long TotalBytes { get; }

    public long Position { get; private set; }

    public int Read(byte[] buffer, int offset, int count) => Read(buffer.AsSpan(offset, count));

    /// <summary>Fills <paramref name="destination"/> as far as possible; returns 0 at the end of the last part.</summary>
    public int Read(Span<byte> destination)
    {
        var total = 0;
        while (total < destination.Length)
        {
            if (_remainingInPart == 0 && !NextPart())
            {
                break;
            }

            var want = (int)Math.Min(destination.Length - total, _remainingInPart);
            var n = _current!.Read(destination.Slice(total, want));
            if (n == 0)
            {
                // The file is shorter than its header said; treat the rest of this part as absent.
                _remainingInPart = 0;
                continue;
            }

            total += n;
            _remainingInPart -= n;
            Position += n;
        }

        return total;
    }

    public void Dispose()
    {
        _current?.Dispose();
        _current = null;
        _index = _parts.Count;
    }

    private bool NextPart()
    {
        _current?.Dispose();
        _current = null;
        while (++_index < _parts.Count)
        {
            var part = _parts[_index];
            if (part.DataBytes == 0)
            {
                continue;
            }

            _current = new FileStream(part.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            _current.Position = part.DataOffset;
            _remainingInPart = part.DataBytes;
            return true;
        }

        return false;
    }
}
