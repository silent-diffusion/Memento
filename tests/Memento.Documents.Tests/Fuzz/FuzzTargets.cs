using System.Text;
using Memento.Documents.Agenda;
using Memento.Documents.Agenda.Ocr;
using Memento.Documents.Agenda.OpenXml;
using Memento.Documents.Agenda.Pdf;
using Memento.Documents.Agenda.Tables;
using Memento.Documents.Agenda.Text;
using Memento.Documents.Render;
using Memento.Documents.Tests.Support;

namespace Memento.Documents.Tests.Fuzz;

/// <summary>The parsers <see cref="AgendaFuzzTests"/> mutates inputs for, each with its seed fixtures and fixed seed.</summary>
internal static class FuzzTargets
{
    public static IReadOnlyList<string> Names { get; } = ["docx", "xlsx", "pdf", "image", "delimited", "markdown", "text", "pasted", "html"];

    public static FuzzTarget Get(string name) => name switch
    {
        "docx" => new FuzzTarget(
            name,
            0x0D0C,
            ["board-table.docx", "messy-mixed.docx", "expected/meeting-minutes.corporate.docx"],
            PackageOrBytes,
            (bytes, fixture, ct) => new DocxAgendaParser().ParseAsync(new MemoryStream(bytes), Options(fixture), ct)),
        "xlsx" => new FuzzTarget(
            name,
            0x0715,
            ["simple-list.xlsx", "planning-second-sheet.xlsx"],
            PackageOrBytes,
            (bytes, fixture, ct) => new XlsxAgendaParser().ParseAsync(new MemoryStream(bytes), Options(fixture), ct)),
        "pdf" => new FuzzTarget(
            name,
            0x0BDF,
            ["numbered-merge.pdf", "two-column.pdf", "expected/meeting-minutes.corporate.print.edge.pdf"],
            ByteMutator.Mutate,
            (bytes, fixture, ct) => new PdfAgendaParser([Ocr()], new WindowsPdfPageRenderer()).ParseAsync(new MemoryStream(bytes), Options(fixture), ct)),
        "image" => new FuzzTarget(
            name,
            0x1316,
            ["ocr-clean.png", "ocr-photo.png", "ocr-small.png", "ocr-photo.jpg"],
            ByteMutator.Mutate,
            (bytes, fixture, ct) => new ImageAgendaParser([Ocr()]).ParseAsync(new MemoryStream(bytes), Options(fixture), ct)),
        "delimited" => new FuzzTarget(
            name,
            0x0C5F,
            ["board-semicolon.csv", "workshop-quoted.csv", "committee-ambiguous.tsv"],
            ByteMutator.Mutate,
            (bytes, fixture, ct) => new DelimitedAgendaParser().ParseAsync(new MemoryStream(bytes), Options(fixture), ct)),
        "markdown" => new FuzzTarget(
            name,
            0x0A4D,
            ["offsite-nested.md", "retro-table.md"],
            ByteMutator.Mutate,
            (bytes, fixture, ct) => new MarkdownAgendaParser().ParseAsync(new MemoryStream(bytes), Options(fixture), ct)),
        "text" => new FuzzTarget(
            name,
            0x07E7,
            ["kickoff-crlf.txt", "standup-cr.txt"],
            ByteMutator.Mutate,
            (bytes, fixture, ct) => new PlainTextAgendaParser().ParseAsync(new MemoryStream(bytes), Options(fixture), ct)),
        "pasted" => new FuzzTarget(
            name,
            0x9A57,
            ["planning-email.paste.txt", "workshop-day.paste.txt"],
            ByteMutator.Mutate,
            (bytes, _, ct) => Agendas.Importer().ParseTextAsync(Encoding.UTF8.GetString(bytes), null, ct)),
        "html" => new FuzzTarget(
            name,
            0x47F1,
            ["expected/meeting-minutes.corporate.viewer.html", "expected/meeting-minutes.academic.viewer.html", "expected/meeting-minutes.minimal.viewer.html", "expected/all-shapes.viewer.html"],
            ByteMutator.Mutate,
            (bytes, _, _) =>
            {
                var html = Encoding.UTF8.GetString(bytes);
                HtmlToBlocks.ParsePaper(html);
                HtmlToBlocks.ParseBlocks(html);
                return Task.CompletedTask;
            }),
        _ => throw new ArgumentOutOfRangeException(nameof(name), name, "No fuzz target by that name."),
    };

    /// <summary>Whether <paramref name="exception"/> is what <paramref name="target"/> may fail with.</summary>
    public static bool IsAllowed(string target, Exception exception) =>
        target != "html" && exception is AgendaImportException;

    private static (byte[] Bytes, string Description) PackageOrBytes(byte[] input, Random random) =>
        random.Next(10) < 8 ? PackageMutator.Mutate(input, random) : ByteMutator.Mutate(input, random);

    private static AgendaParseOptions Options(string fixture) => new() { FileName = Path.GetFileName(fixture) };

    private static FakeOcrEngine Ocr() =>
        new("fake", available: true, FakeOcrEngine.Lines("Agenda", "1. Welcome", "2. Budget review", "3. Next steps"));
}
