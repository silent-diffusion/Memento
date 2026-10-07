using Memento.Documents.Agenda;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests;

public sealed class CancellationTests
{
    public static TheoryData<string> Fixtures => new(FixturePaths.All().Where(n => !FixturePaths.IsPasted(n)));

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task ACancelledImportStopsWithoutAResult(string name)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var engine = new FakeOcrEngine("fake", available: true, FakeOcrEngine.Lines("Welcome"));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Agendas.Importer(engine).ImportFileAsync(FixturePaths.Output(name), null, cancelled.Token));
    }

    [Fact]
    public async Task CancellingWhileTheFileIsStillArrivingStopsTheRead()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        using var slow = new UnseekableStream(new MemoryStream(new byte[64 * 1024]), TimeSpan.FromMilliseconds(40));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Agendas.Importer().ImportAsync(slow, new AgendaParseOptions { FileName = "agenda.txt" }, cancel.Token));
    }

    [Fact]
    public async Task CancellingDuringTextRecognitionStopsIt()
    {
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));
        var engine = new FakeOcrEngine("fake", available: true, FakeOcrEngine.Lines("Welcome")) { Delay = TimeSpan.FromSeconds(5) };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Agendas.Importer(engine).ImportAsync(new MemoryStream(Agendas.Fixture("ocr-small.png")), new AgendaParseOptions { FileName = "a.png" }, cancel.Token));
    }

    [Fact]
    public async Task ACancelledPasteStops()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Agendas.Importer().ParseTextAsync("1. Welcome", null, cancelled.Token));
    }
}
