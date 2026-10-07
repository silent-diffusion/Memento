using System.Runtime.CompilerServices;
using System.Text;

namespace Memento.AI.Http;

/// <summary>
/// Reads <c>text/event-stream</c> per the HTML spec subset providers use: <c>event:</c> and <c>data:</c> fields,
/// comment lines (<c>:</c>) ignored, an empty line dispatches; lines end at LF, CR or CRLF. Throws
/// <see cref="TimeoutException"/> when no line arrives within the idle timeout, and
/// <see cref="CloudAnswerTooLongException"/> when a line or an event's data passes <see cref="MaxEventChars"/>, so a
/// stream without line breaks cannot grow without bound.
/// </summary>
internal static class SseReader
{
    /// <summary>Room for an event that carries a whole answer of <see cref="CloudStreamContext.MaxAnswerChars"/> as escaped JSON.</summary>
    public const int MaxEventChars = 2 * CloudStreamContext.MaxAnswerChars;

    public static async IAsyncEnumerable<SseEvent> ReadAsync(Stream stream, TimeSpan idleTimeout, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
        var lines = new LineReader(reader, MaxEventChars);
        var name = string.Empty;
        var data = new StringBuilder();
        while (true)
        {
            string? line;
            using (var idle = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                idle.CancelAfter(idleTimeout);
                try
                {
                    line = await lines.ReadLineAsync(idle.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new TimeoutException("The stream was idle for too long.");
                }
            }

            if (line is null)
            {
                if (data.Length > 0)
                {
                    yield return new SseEvent(name, data.ToString());
                }

                yield break;
            }

            if (line.Length == 0)
            {
                if (data.Length > 0)
                {
                    yield return new SseEvent(name, data.ToString());
                }

                name = string.Empty;
                data.Clear();
                continue;
            }

            if (line[0] == ':')
            {
                continue;
            }

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? string.Empty : line[(colon + 1)..];
            if (value.StartsWith(' '))
            {
                value = value[1..];
            }

            if (field == "event")
            {
                name = value;
            }
            else if (field == "data")
            {
                if (data.Length + 1 + value.Length > MaxEventChars)
                {
                    throw new CloudAnswerTooLongException();
                }

                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(value);
            }
        }
    }

    /// <summary>Lines of at most <c>maxChars</c> characters (<see cref="StreamReader.ReadLineAsync(CancellationToken)"/> has no limit).</summary>
    private sealed class LineReader(StreamReader reader, int maxChars)
    {
        private readonly char[] _buffer = new char[4096];
        private readonly StringBuilder _line = new();
        private int _position;
        private int _length;
        private bool _skipLineFeed;

        public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            _line.Clear();
            while (true)
            {
                if (_position == _length)
                {
                    _position = 0;
                    _length = await reader.ReadAsync(_buffer.AsMemory(), cancellationToken);
                    if (_length == 0)
                    {
                        return _line.Length > 0 ? _line.ToString() : null;
                    }
                }

                while (_position < _length)
                {
                    var c = _buffer[_position++];
                    if (_skipLineFeed)
                    {
                        _skipLineFeed = false;
                        if (c == '\n')
                        {
                            continue;
                        }
                    }

                    if (c == '\n')
                    {
                        return _line.ToString();
                    }

                    if (c == '\r')
                    {
                        _skipLineFeed = true;
                        return _line.ToString();
                    }

                    if (_line.Length >= maxChars)
                    {
                        throw new CloudAnswerTooLongException();
                    }

                    _line.Append(c);
                }
            }
        }
    }
}
