using NAudio.Wave;

namespace Memento.Audio.Mixing;

/// <summary>
/// Converts any channel count to mono or stereo. Mono is the average of all channels; stereo duplicates a mono
/// input, passes stereo through, and for more channels averages even-numbered channels into the left and
/// odd-numbered into the right (never louder than the loudest input channel, so it cannot clip).
/// </summary>
public sealed class ChannelMapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _in;
    private float[] _buffer = [];

    public ChannelMapSampleProvider(ISampleProvider source, int outputChannels)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (outputChannels is not (1 or 2))
        {
            throw new ArgumentOutOfRangeException(nameof(outputChannels), outputChannels, "Output must be mono or stereo.");
        }

        _source = source;
        _in = source.WaveFormat.Channels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, outputChannels);
    }

    public WaveFormat WaveFormat { get; }

    public int Read(float[] buffer, int offset, int count)
    {
        var outCh = WaveFormat.Channels;
        if (_in == outCh)
        {
            return _source.Read(buffer, offset, count);
        }

        var frames = count / outCh;
        var needed = frames * _in;
        if (_buffer.Length < needed)
        {
            _buffer = new float[needed];
        }

        var read = _source.Read(_buffer, 0, needed);
        var got = read / _in;
        for (var f = 0; f < got; f++)
        {
            var frame = _buffer.AsSpan(f * _in, _in);
            if (outCh == 1)
            {
                float sum = 0;
                foreach (var s in frame)
                {
                    sum += s;
                }

                buffer[offset + f] = sum / _in;
            }
            else if (_in == 1)
            {
                buffer[offset + (2 * f)] = frame[0];
                buffer[offset + (2 * f) + 1] = frame[0];
            }
            else
            {
                float left = 0, right = 0;
                for (var c = 0; c < _in; c++)
                {
                    if ((c & 1) == 0)
                    {
                        left += frame[c];
                    }
                    else
                    {
                        right += frame[c];
                    }
                }

                buffer[offset + (2 * f)] = left / ((_in + 1) / 2);
                buffer[offset + (2 * f) + 1] = right / (_in / 2);
            }
        }

        return got * outCh;
    }
}
