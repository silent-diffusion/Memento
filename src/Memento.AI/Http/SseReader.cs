using System.Runtime.CompilerServices;
using System.Text;

namespace Memento.AI.Http;

/// <summary>
/// Reads <c>text/event-stream</c> per the HTML spec subset providers use: <c>event:</c> and <c>data:</c> fields,
/// comment lines (<c>:</c>) ignored, an empty line dispatches. Throws <see cref="TimeoutException"/> when no line
/// arrives within the idle timeout.
/// </summary>
internal static class SseReader
{
    public static async IAsyncEnumerable<SseEvent> ReadAsync(Stream stream, TimeSpan idleTimeout, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 8192, leaveOpen: true);
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
                    line = await reader.ReadLineAsync(idle.Token);
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
                if (data.Length > 0)
                {
                    data.Append('\n');
                }

                data.Append(value);
            }
        }
    }
}
