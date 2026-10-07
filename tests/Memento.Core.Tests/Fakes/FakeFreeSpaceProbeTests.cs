namespace Memento.Core.Tests.Fakes;

public sealed class FakeFreeSpaceProbeTests
{
    [Fact]
    public void KeepsTheFirstQueriesAndCountsAll()
    {
        var probe = new FakeFreeSpaceProbe { FreeBytes = 42 };

        for (var i = 0; i < FakeFreeSpaceProbe.KeptQueries + 500; i++)
        {
            Assert.Equal(42, probe.GetFreeBytes(@"C:\Library"));
        }

        Assert.Equal(FakeFreeSpaceProbe.KeptQueries, probe.Queried.Count);
        Assert.Equal(FakeFreeSpaceProbe.KeptQueries + 500, probe.QueryCount);
    }
}
