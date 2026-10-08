namespace Memento.Documents.Export;

/// <summary>
/// The lock-down the PDF printer applies to the print HTML before WebView2 loads it from a temporary file. The print page
/// needs nothing beyond its own inline stylesheet (system fonts, no images, scripts off), so a Content-Security-Policy that
/// allows only inline styles makes sure no text in a document (a title, a quote, a model's answer) can ever make the printer
/// fetch a remote or network-share resource (which on Windows could also leak the user's NTLM hash to an SMB host).
/// </summary>
public static class PrintPagePolicy
{
    public const string ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; base-uri 'none'; form-action 'none'";

    private const string Charset = "<meta charset=\"utf-8\">";

    /// <summary>The print HTML with the policy as the first element after <c>&lt;meta charset&gt;</c>.</summary>
    /// <exception cref="ArgumentException">The HTML is not the renderer's print page (no <c>&lt;meta charset="utf-8"&gt;</c>).</exception>
    public static string Apply(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        var index = html.IndexOf(Charset, StringComparison.Ordinal);
        if (index < 0)
        {
            throw new ArgumentException("The print page must start its head with <meta charset=\"utf-8\"> so the policy governs everything after it.", nameof(html));
        }

        var at = index + Charset.Length;
        return html.Insert(at, "\n<meta http-equiv=\"Content-Security-Policy\" content=\"" + ContentSecurityPolicy + "\">");
    }
}
