using System.Text;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Ocr;

namespace Memento.Documents.Tests.Support;

/// <summary>Shortcuts for parsing in tests.</summary>
internal static class Agendas
{
    public static AgendaImporter Importer(params IOcrEngine[] engines) =>
        AgendaImporter.CreateDefault(engines.Length == 0 ? [new FakeOcrEngine("fake", available: false)] : engines);

    public static Task<AgendaParseResult> PasteAsync(string text, AgendaParseOptions? options = null) =>
        Importer().ParseTextAsync(text, options, CancellationToken.None);

    public static Task<AgendaParseResult> ImportAsync(byte[] bytes, string fileName, AgendaImporter? importer = null, AgendaParseOptions? options = null) =>
        (importer ?? Importer()).ImportAsync(new MemoryStream(bytes), (options ?? AgendaParseOptions.Default) with { FileName = fileName }, CancellationToken.None);

    public static Task<AgendaParseResult> ImportTextAsync(string text, string fileName) =>
        ImportAsync(Encoding.UTF8.GetBytes(text), fileName);

    public static byte[] Fixture(string name) => File.ReadAllBytes(FixturePaths.Output(name));

    /// <summary>The items as "level|text" with a trailing "?" for uncertain ones, for compact assertions.</summary>
    public static string[] Shape(AgendaParseResult result) =>
        result.Items.Select(i => $"{i.Level}|{i.Text}{(i.Uncertain ? "?" : string.Empty)}").ToArray();
}
