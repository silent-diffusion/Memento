using System.Diagnostics;
using System.Text;
using Memento.Documents.Agenda.Ocr;
using Memento.Documents.Agenda.OpenXml;
using Memento.Documents.Agenda.Pdf;
using Memento.Documents.Agenda.Tables;
using Memento.Documents.Agenda.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Documents.Agenda;

/// <summary>
/// The entry point for agenda import (<c>agenda.import</c>, <c>agenda.parseText</c>). It reads the file once, refuses
/// anything over the size limit, sniffs what the content really is (never trusting the extension alone), and hands it
/// to the parser for that kind. Everything happens on this PC; nothing touches the network.
/// </summary>
public sealed partial class AgendaImporter
{
    private readonly List<IAgendaParser> _parsers;
    private readonly ILogger<AgendaImporter> _logger;

    public AgendaImporter(IEnumerable<IAgendaParser> parsers, ILogger<AgendaImporter>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(parsers);
        _parsers = parsers.ToList();
        _logger = logger ?? NullLogger<AgendaImporter>.Instance;
    }

    public IReadOnlyList<IAgendaParser> Parsers => _parsers;

    /// <summary>The importer with every built-in parser; images use <paramref name="ocrEngines"/> (Windows OCR, then Tesseract, by default).</summary>
    public static AgendaImporter CreateDefault(IEnumerable<IOcrEngine>? ocrEngines = null, ILoggerFactory? loggerFactory = null)
    {
        var factory = loggerFactory ?? NullLoggerFactory.Instance;
        var engines = ocrEngines?.ToList() ?? [new WindowsOcrEngine(factory.CreateLogger<WindowsOcrEngine>()), new TesseractOcrEngine()];
        return new AgendaImporter(CreateParsers(engines), factory.CreateLogger<AgendaImporter>());
    }

    /// <summary>One of each built-in parser. Scanned PDFs are rendered with Windows' PDF renderer and read with the OCR engines.</summary>
    public static IReadOnlyList<IAgendaParser> CreateParsers(IEnumerable<IOcrEngine> ocrEngines, IPdfPageRenderer? pdfRenderer = null)
    {
        var engines = ocrEngines.ToList();
        return
        [
            new PlainTextAgendaParser(),
            new MarkdownAgendaParser(),
            new DelimitedAgendaParser(),
            new DocxAgendaParser(),
            new XlsxAgendaParser(),
            new PdfAgendaParser(engines, pdfRenderer ?? new WindowsPdfPageRenderer()),
            new ImageAgendaParser(engines),
        ];
    }

    /// <summary>Imports a file from disk; its size is checked before anything is read.</summary>
    public async Task<AgendaParseResult> ImportFileAsync(string path, AgendaParseOptions? options, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var effective = (options ?? AgendaParseOptions.Default) with { FileName = options?.FileName ?? Path.GetFileName(path) };
        var info = new FileInfo(path);
        if (info.Exists && info.Length > effective.MaxFileBytes)
        {
            throw AgendaErrors.FileTooLarge(effective, info.Length);
        }

        var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);
        await using (stream.ConfigureAwait(false))
        {
            return await ImportAsync(stream, effective, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Imports an agenda from a stream. <see cref="AgendaParseOptions.FileName"/> and <see cref="AgendaParseOptions.ContentType"/>
    /// are hints for messages and for choosing between text formats; the bytes decide the format.
    /// </summary>
    public async Task<AgendaParseResult> ImportAsync(Stream content, AgendaParseOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        var effective = options ?? AgendaParseOptions.Default;
        var started = Stopwatch.GetTimestamp();
        var bytes = await AgendaContent.ReadAsync(content, effective, cancellationToken).ConfigureAwait(false);
        var sniffed = FormatSniffer.Sniff(bytes.Span, effective);
        var parser = ParserFor(sniffed.Kind) ?? throw AgendaErrors.Unsupported(
            effective,
            sniffed.Description,
            "No parser for it is set up in this build. Paste the items as text instead.");

        var result = await parser.ParseAsync(new MemoryStream(bytes.ToArray(), writable: false), effective with { SourceKind = sniffed.Kind }, cancellationToken)
            .ConfigureAwait(false);

        var claimed = FormatSniffer.Claimed(effective);
        if (claimed is { } claimedKind && !SameFamily(claimedKind, sniffed.Kind))
        {
            var warning = new AgendaParseWarning(
                AgendaWarningCodes.ExtensionMismatch,
                $"{AgendaErrors.DisplayName(effective)} is named like {Describe(claimedKind)} but is {sniffed.Description}, so it was read as {sniffed.Description}. Rename the file to match if other apps cannot open it.");
            result = result with { Warnings = [warning, .. result.Warnings] };
        }

        Log(result, bytes.Length, started);
        return result;
    }

    /// <summary>
    /// Parses pasted text. Cells copied from a spreadsheet (tab-separated) are read as a table, Markdown as Markdown,
    /// anything else as plain text. The result's <see cref="AgendaParseResult.Source"/> is <see cref="AgendaSourceKind.PastedText"/>.
    /// </summary>
    public Task<AgendaParseResult> ParseTextAsync(string text, AgendaParseOptions? options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(text);
        cancellationToken.ThrowIfCancellationRequested();
        var effective = (options ?? AgendaParseOptions.Default) with { SourceKind = AgendaSourceKind.PastedText, FileName = null };
        if (Encoding.UTF8.GetByteCount(text) > effective.MaxFileBytes)
        {
            throw AgendaErrors.FileTooLarge(effective with { FileName = null }, Encoding.UTF8.GetByteCount(text));
        }

        if (text.Trim().Length == 0)
        {
            throw AgendaErrors.NoItems(effective);
        }

        return ParseGuard.RunAsync(
            effective,
            "pasted text",
            token =>
            {
                var started = Stopwatch.GetTimestamp();
                AgendaParseResult result;
                if (TextShapes.LooksLikeTabTable(text))
                {
                    result = DelimitedAgendaParser.Parse(text, effective with { SourceKind = AgendaSourceKind.Tsv }, [], token) with
                    {
                        Source = AgendaSourceKind.PastedText,
                        SourceName = null,
                    };
                }
                else if (MarkdownAgendaParser.LooksLikeMarkdown(text))
                {
                    result = MarkdownAgendaParser.Parse(text, effective, [], token);
                }
                else
                {
                    result = PlainTextAgendaParser.Parse(text, effective, [], token);
                }

                Log(result, text.Length, started);
                return result;
            },
            cancellationToken);
    }

    private void Log(AgendaParseResult result, long size, long started)
    {
        var uncertain = result.UncertainCount;
        var milliseconds = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        LogImported(_logger, result.Source, size, result.Items.Count, uncertain, result.Warnings.Count, milliseconds);
    }

    private IAgendaParser? ParserFor(AgendaSourceKind kind) => _parsers.FirstOrDefault(p => p.Kinds.Contains(kind));

    private static bool SameFamily(AgendaSourceKind claimed, AgendaSourceKind actual) =>
        claimed == actual || (IsText(claimed) && IsText(actual));

    private static bool IsText(AgendaSourceKind kind) =>
        kind is AgendaSourceKind.Text or AgendaSourceKind.PastedText or AgendaSourceKind.Markdown or AgendaSourceKind.Csv or AgendaSourceKind.Tsv;

    private static string Describe(AgendaSourceKind kind) => kind switch
    {
        AgendaSourceKind.Docx => "a Word document",
        AgendaSourceKind.Xlsx => "an Excel workbook",
        AgendaSourceKind.Pdf => "a PDF",
        AgendaSourceKind.Image => "an image",
        AgendaSourceKind.Csv => "a CSV file",
        AgendaSourceKind.Tsv => "a TSV file",
        AgendaSourceKind.Markdown => "a Markdown file",
        _ => "a text file",
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "Agenda parsed locally from {Source} ({Bytes} bytes): {Items} items, {Uncertain} uncertain, {Warnings} warnings in {Milliseconds:0} ms")]
    private static partial void LogImported(ILogger logger, AgendaSourceKind source, long bytes, int items, int uncertain, int warnings, double milliseconds);
}
