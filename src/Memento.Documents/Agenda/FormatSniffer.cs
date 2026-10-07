using System.IO.Compression;
using System.Text;
using Memento.Documents.Agenda.Ocr;
using Memento.Documents.Agenda.Text;

namespace Memento.Documents.Agenda;

/// <summary>
/// Decides what a file really is from its bytes (magic numbers, the parts inside a ZIP package, text versus binary).
/// The extension only chooses between text formats, which bytes cannot tell apart.
/// </summary>
internal static class FormatSniffer
{
    public static SniffResult Sniff(ReadOnlySpan<byte> bytes, AgendaParseOptions options)
    {
        if (bytes.Length == 0)
        {
            throw AgendaErrors.NoText(options, "is empty", "Choose the file that holds the agenda, or paste the items as text.");
        }

        var head = bytes[..Math.Min(bytes.Length, 1024)];
        if (head.IndexOf("%PDF-"u8) >= 0)
        {
            return new SniffResult(AgendaSourceKind.Pdf, "a PDF");
        }

        if (ImageFormats.Detect(bytes) is { } image)
        {
            if (image is ImageFormats.Gif or ImageFormats.Webp)
            {
                throw AgendaErrors.Unsupported(options, $"a {image} image", "Save it as PNG or JPEG and import it again.");
            }

            return new SniffResult(AgendaSourceKind.Image, $"a {image} image");
        }

        if (bytes.StartsWith("PK\x03\x04"u8))
        {
            return SniffPackage(bytes, options);
        }

        if (bytes.StartsWith((ReadOnlySpan<byte>)[0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1]))
        {
            if (bytes.IndexOf(Encoding.Unicode.GetBytes("EncryptionInfo")) >= 0)
            {
                throw AgendaErrors.Protected(options);
            }

            throw AgendaErrors.Unsupported(
                options,
                "an older Word or Excel file (.doc or .xls)",
                "Open it in Word or Excel, save it as .docx or .xlsx, and import that.");
        }

        if (bytes.StartsWith("{\\rtf"u8))
        {
            throw AgendaErrors.Unsupported(options, "a Rich Text (RTF) file", "Save it as .docx or plain text, or paste the items as text.");
        }

        if (TextDecoder.LooksLikeText(bytes))
        {
            var start = Encoding.UTF8.GetString(head).TrimStart('﻿', ' ', '\t', '\r', '\n').ToLowerInvariant();
            if (start.StartsWith("<!doctype html", StringComparison.Ordinal) || start.StartsWith("<html", StringComparison.Ordinal))
            {
                throw AgendaErrors.Unsupported(options, "a web page (HTML)", "Copy the agenda from the page and paste it as text.");
            }

            return new SniffResult(TextKind(bytes, options), "text");
        }

        throw AgendaErrors.Unsupported(
            options,
            "not a format Memento can read",
            "Agendas can be Word, PDF, Excel, CSV, Markdown or text files, or a photo (PNG, JPEG, BMP, TIFF or HEIC).");
    }

    /// <summary>The kind a file name or content type claims, if it names one.</summary>
    public static AgendaSourceKind? Claimed(AgendaParseOptions options)
    {
        var extension = AgendaResults.FileExtension(options);
        return extension switch
        {
            ".docx" or ".docm" or ".dotx" => AgendaSourceKind.Docx,
            ".xlsx" or ".xlsm" or ".xltx" => AgendaSourceKind.Xlsx,
            ".pdf" => AgendaSourceKind.Pdf,
            ".png" or ".jpg" or ".jpeg" or ".jpe" or ".jfif" or ".bmp" or ".dib" or ".tif" or ".tiff" or ".heic" or ".heif" => AgendaSourceKind.Image,
            ".csv" => AgendaSourceKind.Csv,
            ".tsv" or ".tab" => AgendaSourceKind.Tsv,
            ".md" or ".markdown" or ".mdown" or ".mkd" => AgendaSourceKind.Markdown,
            ".txt" or ".text" => AgendaSourceKind.Text,
            _ => null,
        };
    }

    private static AgendaSourceKind TextKind(ReadOnlySpan<byte> bytes, AgendaParseOptions options)
    {
        var claimed = Claimed(options);
        if (claimed is AgendaSourceKind.Markdown or AgendaSourceKind.Csv or AgendaSourceKind.Tsv or AgendaSourceKind.Text)
        {
            return claimed.Value;
        }

        if (AgendaResults.HasContentType(options.ContentType, "text/markdown", "text/x-markdown"))
        {
            return AgendaSourceKind.Markdown;
        }

        if (AgendaResults.HasContentType(options.ContentType, "text/csv", "application/csv"))
        {
            return AgendaSourceKind.Csv;
        }

        if (AgendaResults.HasContentType(options.ContentType, "text/tab-separated-values"))
        {
            return AgendaSourceKind.Tsv;
        }

        // No usable name (or a binary name on a text file): look at the text itself.
        var text = TextDecoder.Decode(bytes[..Math.Min(bytes.Length, 65536)], out _);
        if (TextShapes.LooksLikeTabTable(text))
        {
            return AgendaSourceKind.Tsv;
        }

        return MarkdownAgendaParser.LooksLikeMarkdown(text) ? AgendaSourceKind.Markdown : AgendaSourceKind.Text;
    }

    private static SniffResult SniffPackage(ReadOnlySpan<byte> bytes, AgendaParseOptions options)
    {
        List<string> names;
        try
        {
            using var zip = new ZipArchive(new MemoryStream(bytes.ToArray(), writable: false), ZipArchiveMode.Read);
            names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).ToList();
        }
        catch (InvalidDataException e)
        {
            throw AgendaErrors.Unreadable(options, "a Word or Excel file", e);
        }

        if (names.Any(n => n.StartsWith("word/", StringComparison.OrdinalIgnoreCase)))
        {
            return new SniffResult(AgendaSourceKind.Docx, "a Word document");
        }

        if (names.Any(n => n.StartsWith("xl/", StringComparison.OrdinalIgnoreCase)))
        {
            return new SniffResult(AgendaSourceKind.Xlsx, "an Excel workbook");
        }

        if (names.Any(n => n.StartsWith("ppt/", StringComparison.OrdinalIgnoreCase)))
        {
            throw AgendaErrors.Unsupported(options, "a PowerPoint presentation", "Copy the agenda slide's text and paste it, or save the slide as a picture and import that.");
        }

        if (names.Contains("content.xml") && names.Contains("mimetype"))
        {
            throw AgendaErrors.Unsupported(options, "an OpenDocument file", "Save it as .docx or .xlsx and import that.");
        }

        throw AgendaErrors.Unsupported(options, "a ZIP archive", "Unzip it and import the agenda file itself.");
    }
}
