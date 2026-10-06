using Memento.Core.Host;

namespace Memento.Core.Tests.Fakes;

/// <summary>Free space under test control. Thread-safe: the disk watch samples it from a timer.</summary>
internal sealed class FakeFreeSpaceProbe : IFreeSpaceProbe
{
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

    public long? GetFreeBytes(string path)
    {
        lock (_gate)
        {
            _queried.Add(path);
            return _freeBytes;
        }
    }
}
