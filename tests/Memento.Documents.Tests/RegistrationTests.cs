using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Ocr;
using Microsoft.Extensions.DependencyInjection;

namespace Memento.Documents.Tests;

public sealed class RegistrationTests
{
    [Fact]
    public async Task AddAgendaImportRegistersTheImporterWithWindowsOcrFirst()
    {
        var services = new ServiceCollection().AddAgendaImport().AddAgendaImport();
        await using var provider = services.BuildServiceProvider();

        var engines = provider.GetServices<IOcrEngine>().ToList();
        var importer = provider.GetRequiredService<AgendaImporter>();

        Assert.Equal(["windows", "tesseract"], engines.Select(e => e.Id).ToArray());
        Assert.Equal(7, importer.Parsers.Count);
        var result = await importer.ParseTextAsync("1. Welcome\n2. Close", null, CancellationToken.None);
        Assert.Equal(2, result.Items.Count);
    }
}
