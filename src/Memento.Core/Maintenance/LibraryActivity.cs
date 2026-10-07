using Memento.Core.Bridge;

namespace Memento.Core.Maintenance;

/// <summary>
/// Library-wide work in progress (imports, exports, reclaim, a library move), so a move never runs under another
/// job and nothing starts while the library is being moved.
/// </summary>
public sealed class LibraryActivity
{
    public const string Import = "import";
    public const string Export = "export";
    public const string Reclaim = "reclaim";
    public const string Move = "move";

    private readonly Dictionary<string, int> _running = new(StringComparer.Ordinal);

    public bool IsMoving
    {
        get
        {
            lock (_running)
            {
                return _running.GetValueOrDefault(Move) > 0;
            }
        }
    }

    /// <summary>What runs now, in words for a <c>library.busy</c> message, or <c>null</c>.</summary>
    public string? Describe()
    {
        lock (_running)
        {
            return _running.Where(r => r.Value > 0).Select(r => r.Key switch
            {
                Import => "an import",
                Export => "an export",
                Reclaim => "making recordings smaller",
                _ => "a library move",
            }).FirstOrDefault();
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
