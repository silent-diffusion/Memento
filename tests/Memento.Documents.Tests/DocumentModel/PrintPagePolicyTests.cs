using Memento.Documents.Export;
using Memento.Documents.Render;
using Memento.Documents.Styling;
using Memento.Documents.Tests.DocumentModel.Support;

namespace Memento.Documents.Tests.DocumentModel;

/// <summary>Security audit 2026-10-07, SA-03: the PDF printer's page can fetch nothing.</summary>
public sealed class PrintPagePolicyTests
{
    [Fact]
    public void ThePolicyIsTheFirstThingAfterTheCharset()
    {
        var html = PrintPagePolicy.Apply(new DocumentHtmlRenderer().RenderPrintHtml(SampleDocuments.MeetingMinutes(), BuiltInStyles.Corporate));

        var charset = html.IndexOf("<meta charset=\"utf-8\">", StringComparison.Ordinal);
        var policy = html.IndexOf("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; style-src 'unsafe-inline'", StringComparison.Ordinal);
        Assert.True(charset >= 0);
        Assert.Equal(charset + "<meta charset=\"utf-8\">".Length + 1, policy);
        Assert.True(policy < html.IndexOf("<title>", StringComparison.Ordinal));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(html, "Content-Security-Policy"));
    }

    [Fact]
    public void HostileDocumentTextStaysText()
    {
        var hostile = "<img src=\"\\\\203.0.113.9\\share\\x.png\"><link rel=stylesheet href=https://example.invalid/x.css><script>alert(1)</script>";
        var document = SampleDocuments.MeetingMinutes() with { Title = hostile };

        var html = PrintPagePolicy.Apply(new DocumentHtmlRenderer().RenderPrintHtml(document, BuiltInStyles.Corporate));

        Assert.DoesNotContain("<img", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<link", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("<script", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("&lt;img", html, StringComparison.Ordinal);
    }

    [Fact]
    public void RefusesHtmlThatIsNotThePrintPage() =>
        Assert.Throws<ArgumentException>(() => PrintPagePolicy.Apply("<html><head><title>x</title></head></html>"));
}
