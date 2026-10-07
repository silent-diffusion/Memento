using Memento.Audio.Codecs;

namespace Memento.Worker;

/// <summary>
/// Hands out the samples of consecutive overlapping windows from a forward-only decoded stream, keeping only the
/// part a later window still needs (at most one window plus its overlap in memory).
/// </summary>
internal sealed class WindowReader(DecodedAudio audio)
{
    private readonly List<float> _buffer = [];
    private readonly float[] _block = new float[TrackAudio.SampleRate * 5];
    private long _bufferStart;
    private bool _ended;

    /// <summary>The samples from <paramref name="start"/> to <paramref name="end"/> seconds (fewer at the end of the track).</summary>
    public float[] Read(double start, double end)
    {
        var from = (long)Math.Round(start * TrackAudio.SampleRate);
        var to = (long)Math.Round(end * TrackAudio.SampleRate);
        if (from < _bufferStart)
        {
            throw new InvalidOperationException("Windows must be read in order.");
        }

        // Drop what no window needs any more.
        var drop = (int)Math.Min(from - _bufferStart, _buffer.Count);
        _buffer.RemoveRange(0, drop);
        _bufferStart += drop;
        while (_bufferStart + _buffer.Count < from && !_ended)
        {
            var skip = Fill();
            var discard = (int)Math.Min(from - _bufferStart, _buffer.Count);
            _buffer.RemoveRange(0, discard);
            _bufferStart += discard;
            if (skip == 0)
            {
                break;
            }
        }

        while (_bufferStart + _buffer.Count < to && !_ended)
        {
            Fill();
        }

        var count = (int)Math.Max(0, Math.Min(to, _bufferStart + _buffer.Count) - from);
        return _buffer.GetRange((int)(from - _bufferStart), count).ToArray();
    }

    private int Fill()
    {
        var n = audio.Read(_block, 0, _block.Length);
        if (n <= 0)
        {
            _ended = true;
            return 0;
        }

        _buffer.AddRange(_block.AsSpan(0, n));
        return n;
    }
}
