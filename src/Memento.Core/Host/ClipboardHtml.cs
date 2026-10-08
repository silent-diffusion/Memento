using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Memento.Core.Host;

/// <summary>
/// The Windows clipboard's <c>HTML Format</c> (CF_HTML): a short header with UTF-8 byte offsets of the HTML and of the
/// fragment to paste, then the page with the fragment between <c>&lt;!--StartFragment--&gt;</c> and
/// <c>&lt;!--EndFragment--&gt;</c>. Word and Outlook read the page's styles and paste the fragment formatted.
/// </summary>
public static partial class ClipboardHtml
{
    public const string StartMarker = "<!--StartFragment-->";
    public const string EndMarker = "<!--EndFragment-->";

    private static readonly CompositeFormat HeaderFormat = CompositeFormat.Parse("Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\nStartFragment:{2:D10}\r\nEndFragment:{3:D10}\r\n");

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <summary>
    /// Wraps <paramref name="html"/>: a whole page (with <c>&lt;body&gt;</c>) keeps its head and styles and its body becomes
    /// the fragment; anything else becomes the body of a minimal page.
    /// </summary>
    public static string Wrap(string html)
    {
        ArgumentNullException.ThrowIfNull(html);
        string page;
        var open = BodyOpen().Match(html);
        var close = open.Success ? html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase) : -1;
        if (open.Success && close >= open.Index + open.Length)
        {
            var bodyStart = open.Index + open.Length;
            page = html[..bodyStart] + StartMarker + html[bodyStart..close] + EndMarker + html[close..];
        }
        else
        {
            page = "<html><head><meta charset=\"utf-8\"></head><body>" + StartMarker + html + EndMarker + "</body></html>";
        }

        var headerLength = Utf8.GetByteCount(Header(0, 0, 0, 0));
        var startFragment = headerLength + Utf8.GetByteCount(page.AsSpan(0, page.IndexOf(StartMarker, StringComparison.Ordinal) + StartMarker.Length));
        var endFragment = headerLength + Utf8.GetByteCount(page.AsSpan(0, page.LastIndexOf(EndMarker, StringComparison.Ordinal)));
        var endHtml = headerLength + Utf8.GetByteCount(page);
        return Header(headerLength, endHtml, startFragment, endFragment) + page;
    }

    private static string Header(int startHtml, int endHtml, int startFragment, int endFragment) =>
        string.Format(CultureInfo.InvariantCulture, HeaderFormat, startHtml, endHtml, startFragment, endFragment);

    [GeneratedRegex("<body\\b[^>]*>", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BodyOpen();
}
