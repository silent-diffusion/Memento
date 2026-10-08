using Memento.Core.Host;

namespace Memento.Core.Tests.Fakes;

/// <summary>Free space under test control. Thread-safe: the disk watch samples it from a timer.</summary>
internal sealed class FakeFreeSpaceProbe : IFreeSpaceProbe
{
    /// <summary>
    /// Only the first queries are kept: the disk watch samples every 20 ms in the test host, and keeping every path
    /// grew the 8-hour simulated soak's managed heap by 15 MB (1.4 million entries) as if Memento leaked.
    /// </summary>
    public const int KeptQueries = 1000;

    private readonly object _gate = new();
    private readonly List<string> _queried = [];
    private long? _freeBytes;

    public long? FreeBytes
    {
        get
        {
            lock (_gate)
            {
                return _freeBytes;
            }
        }

        set
        {
            lock (_gate)
            {
                _freeBytes = value;
            }
        }
    }

    public List<string> Queried
    {
        get
        {
            lock (_gate)
            {
                return [.. _queried];
            }
        }
    }

    /// <summary>Every query, also those past <see cref="KeptQueries"/>.</summary>
    public long QueryCount { get; private set; }

    public long? GetFreeBytes(string path)
    {
        lock (_gate)
        {
            QueryCount++;
            if (_queried.Count < KeptQueries)
            {
                _queried.Add(path);
            }

            return _freeBytes;
        }
    }
}
