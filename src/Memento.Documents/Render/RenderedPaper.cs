namespace Memento.Documents.Render;

/// <summary>
/// A rendered paper: <see cref="Css"/> is the same for every paper (insert it once); <see cref="Html"/> is the
/// <c>&lt;article class="paper …"&gt;</c> element, whose style variables carry the document style.
/// </summary>
public sealed record RenderedPaper(PaperMode Mode, string Css, string Html)
{
    /// <summary>A complete HTML page holding just this paper, for snapshots and for opening in a browser.</summary>
    public string ToHtmlPage(string title) =>
        "<!doctype html>\n<html lang=\"en\">\n<head>\n<meta charset=\"utf-8\">\n<title>" + HtmlText.Escape(title) + "</title>\n<style>\n"
        + "body{margin:0;padding:32px;background:#EFEDE8}\n" + Css + "</style>\n</head>\n<body>\n" + Html + "</body>\n</html>\n";
}
