using Memento.Core.Host;

namespace Memento.Core.Tests.Fakes;

internal sealed class FakeFreeSpaceProbe : IFreeSpaceProbe
{
    public long? FreeBytes { get; set; }

    public List<string> Queried { get; } = [];

    public long? GetFreeBytes(string path)
    {
        Queried.Add(path);
        return FreeBytes;
    }
}
