namespace Memento.Core.Bridge.Contracts;

/// <summary>Paper markup for the UI to insert (<c>styles.sampleHtml</c>, <c>generation.previewHtml</c>, <c>documents.renderHtml</c>).</summary>
/// <param name="Html">The <c>&lt;article class="paper"&gt;</c> element (view) or a whole HTML page (print).</param>
/// <param name="Css">The paper stylesheet, the same for every paper; insert it once.</param>
public sealed record HtmlResult(string Html)
{
    public string? Css { get; init; }
}
