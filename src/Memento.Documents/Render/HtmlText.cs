using System.Globalization;
using System.Text;

namespace Memento.Documents.Render;

/// <summary>Deterministic escaping and number formatting for the paper markup.</summary>
internal static class HtmlText
{
    /// <summary>Escapes <c>&amp; &lt; &gt; " '</c> only; every other character is written as UTF-8.</summary>
    public static string Escape(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': builder.Append("&amp;"); break;
                case '<': builder.Append("&lt;"); break;
                case '>': builder.Append("&gt;"); break;
                case '"': builder.Append("&quot;"); break;
                case '\'': builder.Append("&#39;"); break;
                case '\r': break;
                default: builder.Append(c); break;
            }
        }

        return builder.ToString();
    }

    /// <summary>Escapes text and turns line breaks into <c>&lt;br&gt;</c>.</summary>
    public static string EscapeWithBreaks(string? value) => Escape(value).Replace("\n", "<br>", StringComparison.Ordinal);

    /// <summary>A CSS string literal: <c>"Design review"</c>, with quotes, backslashes and line breaks escaped.</summary>
    public static string CssString(string value)
    {
        var builder = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\n': builder.Append("\\A "); break;
                case '\r': break;
                case '<': builder.Append("\\3C "); break;
                default: builder.Append(c); break;
            }
        }

        return builder.Append('"').ToString();
    }

    /// <summary>Up to three decimals, invariant, no trailing zeros: <c>14</c>, <c>12.833</c>, <c>0.9</c>.</summary>
    public static string Number(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);
}
