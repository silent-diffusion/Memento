using System.Text;

namespace Memento.Core.Workers;

/// <summary>
/// Reads JSON-lines protocol lines from a worker with a length limit, so a worker (or a native library writing to its
/// stdout) cannot make the host buffer an unbounded line. A line longer than the limit is read to its end and dropped:
/// <see cref="ReadLineAsync"/> returns an empty string and <see cref="LastDiscardedLength"/> says how long it was. Lines
/// end at <c>\n</c>, <c>\r</c> or <c>\r\n</c>, as with <see cref="TextReader.ReadLine"/>.
/// </summary>
public sealed class ProtocolLineReader
{
    /// <summary>16 Mi characters: far above the largest result (a diarization of hours of audio is well under 1 MB).</summary>
    public const int DefaultMaxChars = 16 * 1024 * 1024;

    private readonly TextReader _reader;
    private readonly int _maxChars;
    private readonly char[] _buffer = new char[16 * 1024];
    private int _start;
    private int _end;
    private bool _skipLineFeed;
    private bool _ended;
    private StringBuilder? _line;
    private long _length;

    public ProtocolLineReader(TextReader reader, int maxChars = DefaultMaxChars)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxChars, 1);
        _reader = reader;
        _maxChars = maxChars;
    }

    /// <summary>The length of the line the last read dropped for being too long; <c>null</c> when it dropped nothing.</summary>
    public long? LastDiscardedLength { get; private set; }

    /// <summary>The next line; <c>null</c> at the end of the stream.</summary>
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        LastDiscardedLength = null;
        _line = null;
        _length = 0;
        var started = false;
        while (true)
        {
            if (_start == _end)
            {
                if (!_ended)
                {
                    _start = 0;
                    _end = await _reader.ReadAsync(_buffer.AsMemory(), cancellationToken);
                    _ended = _end == 0;
                }

                if (_ended)
                {
                    return started ? Finish() : null;
                }
            }

            if (_skipLineFeed)
            {
                _skipLineFeed = false;
                if (_buffer[_start] == '\n')
                {
                    _start++;
                    continue;
                }
            }

            started = true;
            if (TakeFromBuffer())
            {
                return Finish();
            }
        }
    }

    /// <summary>Moves buffered characters into the line up to its ending; <c>true</c> when the ending was reached.</summary>
    private bool TakeFromBuffer()
    {
        var span = _buffer.AsSpan(_start, _end - _start);
        var stop = span.IndexOfAny('\r', '\n');
        var piece = stop < 0 ? span : span[..stop];
        if (_length + piece.Length <= _maxChars)
        {
            (_line ??= new StringBuilder()).Append(piece);
        }
        else
        {
            _line = null; // Too long: keep counting, stop keeping.
        }

        _length += piece.Length;
        _start += piece.Length;
        if (stop < 0)
        {
            return false;
        }

        _skipLineFeed = span[stop] == '\r';
        _start++;
        return true;
    }

    private string Finish()
    {
        if (_length > _maxChars)
        {
            LastDiscardedLength = _length;
            return string.Empty;
        }

        return _line?.ToString() ?? string.Empty;
    }
}
