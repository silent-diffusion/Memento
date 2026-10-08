using Memento.Core.Host;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Status;

public sealed class OverrideFreeSpaceProbeTests
{
    [Fact]
    public void AFixedNumberIsWhatEveryDriveReports()
    {
        var real = new FakeFreeSpaceProbe { FreeBytes = 500_000_000_000 };
        var probe = new OverrideFreeSpaceProbe("2147483648", real);

        Assert.Equal(2_147_483_648, probe.GetFreeBytes(@"C:\Library"));
        Assert.Equal(2_147_483_648, probe.GetFreeBytes(@"D:\Elsewhere"));
        Assert.Empty(real.Queried);
    }

    [Fact]
    public void AFileIsReadAgainOnEveryCheckSoATestCanFillTheDrive()
    {
        using var directory = new TempDirectory();
        var file = directory.File("free.txt");
        var real = new FakeFreeSpaceProbe { FreeBytes = 123 };
        var probe = new OverrideFreeSpaceProbe(file, real);

        Assert.Equal(123, probe.GetFreeBytes(directory.Path)); // no file yet: the real drive answers
        File.WriteAllText(file, "9000000000\n");
        Assert.Equal(9_000_000_000, probe.GetFreeBytes(directory.Path));
        File.WriteAllText(file, "100000");
        Assert.Equal(100_000, probe.GetFreeBytes(directory.Path));
        File.WriteAllText(file, "not a number");
        Assert.Equal(123, probe.GetFreeBytes(directory.Path));
    }
}
