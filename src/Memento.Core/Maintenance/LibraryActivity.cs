using Memento.Core.Bridge;

namespace Memento.Core.Maintenance;

/// <summary>
/// Library-wide work in progress (imports, exports, reclaim, a library move, a recording being started), so a move
/// never runs under another job and nothing starts while the library is being moved.
/// </summary>
public sealed class LibraryActivity
{
    public const string Import = "import";
    public const string Export = "export";
    public const string Reclaim = "reclaim";
    public const string Move = "move";

    /// <summary><c>recording.start</c> between its check for a move and the session being registered.</summary>
    public const string RecordingStart = "recordingStart";

    private readonly Dictionary<string, int> _running = new(StringComparer.Ordinal);

    public bool IsMoving => Count(Move) > 0;

    public bool IsStartingRecording => Count(RecordingStart) > 0;

    /// <summary>
    /// What runs now, in words for a <c>library.busy</c> message, or <c>null</c>. <paramref name="except"/> leaves out
    /// one job of that kind (the caller's own).
    /// </summary>
    public string? Describe(string? except = null)
    {
        lock (_running)
        {
            return _running
                .Where(r => r.Value - (r.Key == except ? 1 : 0) > 0)
                .Select(r => r.Key switch
                {
                    Import => "an import",
                    Export => "an export",
                    Reclaim => "making recordings smaller",
                    RecordingStart => "a recording is starting",
                    _ => "a library move",
                })
                .FirstOrDefault();
        }
    }

    public IDisposable Begin(string kind)
    {
        lock (_running)
        {
            _running[kind] = _running.GetValueOrDefault(kind) + 1;
        }

        return new Token(this, kind);
    }

    /// <exception cref="BridgeException"><c>library.busy</c> while the library is being moved.</exception>
    public void ThrowIfMoving()
    {
        if (IsMoving)
        {
            throw new BridgeException(
                DomainErrorCodes.LibraryBusy,
                "The library is being moved to its new folder. Nothing was started; try again when the move has finished.");
        }
    }

    /// <summary>
    /// Marks a recording as starting and refuses it while the library is being moved. Dispose the result once the
    /// session is registered (or the start failed). A move marks itself before it checks for a recording, so whichever
    /// of the two comes second sees the other.
    /// </summary>
    /// <exception cref="BridgeException"><c>library.busy</c> while the library is being moved.</exception>
    public IDisposable BeginRecordingStart()
    {
        var starting = Begin(RecordingStart);
        if (IsMoving)
        {
            starting.Dispose();
            throw new BridgeException(
                DomainErrorCodes.LibraryBusy,
                "The library is being copied to its new folder, so a recording can't start right now. Nothing was started. Recording is possible again as soon as the move has finished; its progress is in Settings › Storage and history.",
                Move);
        }

        return starting;
    }

    private int Count(string kind)
    {
        lock (_running)
        {
            return _running.GetValueOrDefault(kind);
        }
    }

    private void End(string kind)
    {
        lock (_running)
        {
            _running[kind] = Math.Max(0, _running.GetValueOrDefault(kind) - 1);
        }
    }

    private sealed class Token(LibraryActivity owner, string kind) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                owner.End(kind);
            }
        }
    }
}
