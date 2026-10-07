namespace Memento.Core.Bridge.Contracts;

/// <summary>
/// Paper markup for the UI (<c>styles.sampleHtml</c>, <c>generation.previewHtml</c>, <c>documents.renderHtml</c>): the
/// <c>article.paper</c> element only. The UI bundles the paper stylesheet itself (a verbatim copy of
/// <c>PaperCss.Stylesheet</c>; its CSP forbids inline styles). <c>documents.renderHtml</c> in <c>print</c> mode returns the
/// whole print page instead (the page the PDF is printed from).
/// </summary>
public sealed record HtmlResult(string Html);
