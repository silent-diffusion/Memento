using System.Text;

namespace Memento.AI.Local;

/// <summary>
/// Coalesces one prompt's decoded pieces into a few <c>progress</c> lines (ARCHITECTURE.md §8, Live output): the first
/// piece goes out at once, then one line at most every <see cref="Interval"/> or every <see cref="MaxPieces"/> pieces,
/// and <see cref="Flush"/> sends the rest when the answer ends. The pieces are concatenated unchanged, so the lines add
/// up to the answer exactly. <see cref="Add"/> runs on the decode thread; it only appends and reads the clock, and the
/// line itself is written by <c>send</c>.
/// </summary>
public sealed class LocalTokenCoalescer
{
    /// <summary>The longest a piece waits before it is sent, while more pieces arrive.</summary>
    public static readonly TimeSpan Interval = TimeSpan.FromMilliseconds(40);

    /// <summary>A line goes out at the latest after this many pieces.</summary>
    public const int MaxPieces = 8;

    private readonly TimeProvider _time;
    private readonly Action<string, int, TimeSpan> _send;
    private readonly StringBuilder _pending = new();
    private int _pendingPieces;
    private long _first;
    private long _lastSent;
    private bool _started;

    /// <param name="send">Receives the text since the last line, the pieces so far and the time since the first piece.</param>
    public LocalTokenCoalescer(TimeProvider time, Action<string, int, TimeSpan> send)
    {
        ArgumentNullException.ThrowIfNull(time);
        ArgumentNullException.ThrowIfNull(send);
        _time = time;
        _send = send;
    }

    /// <summary>Pieces received so far.</summary>
    public int Pieces { get; private set; }

    /// <summary>Lines sent so far.</summary>
    public int Lines { get; private set; }

    public void Add(string piece)
    {
        if (string.IsNullOrEmpty(piece))
        {
            return;
        }

        var now = _time.GetTimestamp();
        Pieces++;
        _pending.Append(piece);
        _pendingPieces++;
        if (!_started)
        {
            _started = true;
            _first = now;
            Send(now);
            return;
        }

        if (_pendingPieces >= MaxPieces || _time.GetElapsedTime(_lastSent, now) >= Interval)
        {
            Send(now);
        }
    }

    /// <summary>Sends what is still waiting (the end of the answer).</summary>
    public void Flush()
    {
        if (_pendingPieces > 0)
        {
            Send(_time.GetTimestamp());
        }
    }

    private void Send(long now)
    {
        var text = _pending.ToString();
        _pending.Clear();
        _pendingPieces = 0;
        _lastSent = now;
        Lines++;
        _send(text, Pieces, _time.GetElapsedTime(_first, now));
    }
}
