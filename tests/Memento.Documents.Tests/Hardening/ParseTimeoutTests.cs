using System.Diagnostics;
using Memento.Documents.Agenda;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.Hardening;

/// <summary>A parse that runs past its time limit is stopped with a specific agenda error, not left running in the host.</summary>
public sealed class ParseTimeoutTests
{
    [Fact]
    public async Task ASlowParseIsStoppedAtTheLimit()
    {
        var engine = new FakeOcrEngine("fake", available: true, FakeOcrEngine.Lines("Welcome")) { Delay = TimeSpan.FromSeconds(30) };
        var options = new AgendaParseOptions { FileName = "photo.png", ParseTimeout = TimeSpan.FromMilliseconds(300) };
        var watch = Stopwatch.StartNew();

        var error = await Assert.ThrowsAsync<AgendaImportException>(() =>
            Agendas.Importer(engine).ImportAsync(new MemoryStream(Agendas.Fixture("ocr-small.png")), options, CancellationToken.None));

        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), $"took {watch.Elapsed.TotalSeconds:0.0} s");
        Assert.Equal(AgendaErrorCodes.Unreadable, error.Code);
        Assert.Equal(
            "photo.png took longer than 0.3 seconds to read as an image, so reading it was stopped. Nothing was imported. Copy just the agenda into a new file and import that, or paste the items as text.",
            error.Message);
    }

    [Fact]
    public async Task CancellingIsStillACancellationNotATimeout()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var engine = new FakeOcrEngine("fake", available: true, FakeOcrEngine.Lines("Welcome")) { Delay = TimeSpan.FromSeconds(30) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Agendas.Importer(engine).ImportAsync(new MemoryStream(Agendas.Fixture("ocr-small.png")), new AgendaParseOptions { FileName = "a.png" }, cancel.Token));
    }

    [Fact]
    public void TheDefaultLimitIsSixtySeconds()
    {
        Assert.Equal(TimeSpan.FromSeconds(60), AgendaParseOptions.Default.ParseTimeout);
    }
}
