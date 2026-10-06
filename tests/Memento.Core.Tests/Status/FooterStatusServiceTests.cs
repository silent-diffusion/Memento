using System.Text.Json;
using Memento.Core.Status;
using Memento.Core.Tests.Fakes;

namespace Memento.Core.Tests.Status;

public sealed class FooterStatusServiceTests : IDisposable
{
    private readonly BridgeTestHost _host = new();

    public void Dispose() => _host.Dispose();

    private FooterStatusService Service => _host.Get<FooterStatusService>();

    [Fact]
    public void ReportsFreeSpaceOfTheLibraryDrive()
    {
        _host.FreeSpace.FreeBytes = 212L * 1024 * 1024 * 1024;

        var status = Service.Compute();

        Assert.Equal(212L * 1024 * 1024 * 1024, status.Storage.FreeBytes);
        Assert.False(status.Storage.LowSpace);
        Assert.Equal(_host.Library.Root, Assert.Single(_host.FreeSpace.Queried));
    }

    [Theory]
    [InlineData(FooterStatusService.LowSpaceThresholdBytes - 1, true)]
    [InlineData(FooterStatusService.LowSpaceThresholdBytes, false)]
    [InlineData(4L * 1024 * 1024 * 1024, true)]
    public void FlagsLowSpaceBelowTenGigabytes(long freeBytes, bool expectedLow)
    {
        _host.FreeSpace.FreeBytes = freeBytes;

        Assert.Equal(expectedLow, Service.Compute().Storage.LowSpace);
    }

    [Fact]
    public void UnreadableDriveIsReportedAsUnknownNotLow()
    {
        _host.FreeSpace.FreeBytes = null;

        var status = Service.Compute();

        Assert.Null(status.Storage.FreeBytes);
        Assert.False(status.Storage.LowSpace);
    }

    [Fact]
    public void EngineIsNotReadyBecauseNoEngineShipsYet()
    {
        var engine = Service.Compute().Engine;

        Assert.False(engine.Ready);
        Assert.Null(engine.Device);
    }

    [Fact]
    public void PublishesOnlyWhenTheStatusChangesUnlessForced()
    {
        _host.FreeSpace.FreeBytes = 100;

        Assert.True(Service.Publish(force: false));
        Assert.False(Service.Publish(force: false));
        Assert.True(Service.Publish(force: true));
        _host.FreeSpace.FreeBytes = 200;
        Assert.True(Service.Publish(force: false));

        Assert.Equal(3, _host.Sink.Posted.Count);
        using var last = JsonDocument.Parse(_host.Sink.Posted[^1]);
        Assert.Equal("status.footer", last.RootElement.GetProperty("event").GetString());
        Assert.Equal(200, last.RootElement.GetProperty("payload").GetProperty("storage").GetProperty("freeBytes").GetInt64());
    }
}
