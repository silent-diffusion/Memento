namespace Memento.Core.Audio;

/// <summary>
/// Streams a WAV file at another sample rate and channel count, by linear interpolation.
/// Equal rates copy samples exactly. Channel mapping: mono is duplicated to every output channel;
/// to mono, all channels are averaged; otherwise channels are folded onto the outputs round-robin and averaged.
/// Linear interpolation is enough for the playback mix; the original tracks keep their native rate.
/// </summary>
internal sealed class ResamplingReader : IDisposable
{
    private const int ChunkFrames = 4096;

    private readonly WavReader _reader;
    private readonly int _inChannels;
    private readonly int _outChannels;
    private readonly double _step;
    private readonly float[] _raw;
    private float[] _frames;
    private long _bufferStart;
    private int _bufferFrames;
    private double _position;
    private bool _endOfInput;

    public ResamplingReader(WavReader reader, int outputRate, int outputChannels)
    {
        _reader = reader;
        _inChannels = reader.Format.Channels;
        _outChannels = outputChannels;
        _step = reader.Format.SampleRate / (double)outputRate;
        _raw = new float[ChunkFrames * _inChannels];
        _frames = new float[(ChunkFrames + 2) * outputChannels];
        OutputFrames = (long)Math.Ceiling(reader.TotalFrames / _step);
    }

    /// <summary>Frames this source yields at the output rate.</summary>
    public long OutputFrames { get; }

    /// <summary>Reads up to <paramref name="frames"/> output frames into <paramref name="destination"/>; returns how many.</summary>
    public int Read(Span<float> destination, int frames)
    {
        var produced = 0;
        while (produced < frames)
        {
            var index = (long)Math.Floor(_position);
            if (index >= _reader.TotalFrames)
            {
                break;
            }

            if (!EnsureBuffered(index + 1))
            {
                if (!EnsureBuffered(index))
                {
                    break;
                }
            }

            var fraction = (float)(_position - index);
            var a = (int)(index - _bufferStart) * _outChannels;
            var hasNext = index + 1 < _bufferStart + _bufferFrames;
            var b = hasNext ? a + _outChannels : a;
            var o = produced * _outChannels;
            for (var c = 0; c < _outChannels; c++)
            {
                var first = _frames[a + c];
                destination[o + c] = fraction == 0f ? first : first + ((_frames[b + c] - first) * fraction);
            }

            produced++;
            _position += _step;
        }

        return produced;
    }

    public void Dispose() => _reader.Dispose();

    /// <summary>Makes sure input frame <paramref name="frame"/> is in the buffer; false if the input ended before it.</summary>
    private bool EnsureBuffered(long frame)
    {
        while (frame >= _bufferStart + _bufferFrames)
        {
            if (_endOfInput)
            {
                return false;
            }

            // Keep the frames from the current read position onward and append a new chunk.
            // Never move the start past the end of what was read, or input frames would be skipped.
            var end = _bufferStart + _bufferFrames;
            var keepFrom = Math.Clamp((long)Math.Floor(_position), _bufferStart, end);
            var keep = (int)(end - keepFrom);
            if (keep > 0)
            {
                Array.Copy(_frames, (int)(keepFrom - _bufferStart) * _outChannels, _frames, 0, keep * _outChannels);
            }

            _bufferStart = keepFrom;
            _bufferFrames = keep;
            var needed = (keep + ChunkFrames) * _outChannels;
            if (_frames.Length < needed)
            {
                Array.Resize(ref _frames, needed);
            }

            var read = _reader.ReadFrames(_raw);
            if (read == 0)
            {
                _endOfInput = true;
                continue;
            }

            MapChannels(_raw.AsSpan(0, read * _inChannels), _frames.AsSpan(keep * _outChannels, read * _outChannels), read);
            _bufferFrames += read;
        }

        return true;
    }

    private void MapChannels(ReadOnlySpan<float> input, Span<float> output, int frames)
    {
        if (_inChannels == _outChannels)
        {
            input.CopyTo(output);
            return;
        }

        for (var f = 0; f < frames; f++)
        {
            var inBase = f * _inChannels;
            var outBase = f * _outChannels;
            if (_inChannels == 1)
            {
                for (var c = 0; c < _outChannels; c++)
                {
                    output[outBase + c] = input[inBase];
                }

                continue;
            }

            if (_outChannels == 1)
            {
                var sum = 0f;
                for (var c = 0; c < _inChannels; c++)
                {
                    sum += input[inBase + c];
                }

                output[outBase] = sum / _inChannels;
                continue;
            }

            for (var c = 0; c < _outChannels; c++)
            {
                var sum = 0f;
                var count = 0;
                for (var source = c; source < _inChannels; source += _outChannels)
                {
                    sum += input[inBase + source];
                    count++;
                }

                output[outBase + c] = count == 0 ? 0f : sum / count;
            }
        }
    }
}
