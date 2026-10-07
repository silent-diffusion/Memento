using System.Text;
using Memento.Documents.Export;

namespace Memento.Documents.Tests.DocumentModel.Support;

/// <summary>Test double for the host's WebView2 printer: records the HTML and options and returns a tiny PDF-shaped payload.</summary>
internal sealed class FakePdfPrinter : IPdfPrinter
{
    public string? Html { get; private set; }

    public PdfPrintOptions? Options { get; private set; }

    public Exception? Failure { get; init; }

    public Task<byte[]> PrintAsync(string html, PdfPrintOptions options, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Failure is not null)
        {
            throw Failure;
        }

        Html = html;
        Options = options;
        return Task.FromResult(Encoding.ASCII.GetBytes($"%PDF-1.7\n% fake {html.Length} chars\n%%EOF\n"));
    }
}
