using System.Globalization;

namespace Memento.Documents.Tests.Support;

/// <summary>
/// A fixture's expected-items file (<c>name.expected</c>): <c># comments</c>, <c>source:</c>, <c>title:</c>,
/// <c>warning:</c> (a code that must be present), <c>cer:</c> (the most text recognition may get wrong), then one
/// item per line, two spaces of indent per level, <c>? </c> before an item that must be marked uncertain, and
/// <c> | time</c> after an item with a time.
/// </summary>
internal sealed record ExpectedAgenda(
    string? Source,
    string? Title,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<ExpectedItem> Items,
    double? MaxCharacterErrorRate)
{
    public static ExpectedAgenda Load(string path)
    {
        string? source = null, title = null;
        double? cer = null;
        var warnings = new List<string>();
        var items = new List<ExpectedItem>();
        foreach (var raw in File.ReadAllLines(path))
        {
            if (raw.Trim().Length == 0 || raw.StartsWith('#'))
            {
                continue;
            }

            if (raw.StartsWith("source: ", StringComparison.Ordinal))
            {
                source = raw[8..].Trim();
            }
            else if (raw.StartsWith("title: ", StringComparison.Ordinal))
            {
                title = raw[7..].Trim();
            }
            else if (raw.StartsWith("warning: ", StringComparison.Ordinal))
            {
                warnings.Add(raw[9..].Trim());
            }
            else if (raw.StartsWith("cer: ", StringComparison.Ordinal))
            {
                cer = double.Parse(raw[5..], CultureInfo.InvariantCulture);
            }
            else
            {
                var indent = raw.Length - raw.TrimStart(' ').Length;
                var text = raw.Trim();
                var uncertain = text.StartsWith("? ", StringComparison.Ordinal);
                if (uncertain)
                {
                    text = text[2..];
                }

                string? time = null;
                var bar = text.LastIndexOf(" | ", StringComparison.Ordinal);
                if (bar > 0)
                {
                    time = text[(bar + 3)..].Trim();
                    text = text[..bar];
                }

                items.Add(new ExpectedItem(text, indent / 2, uncertain, time));
            }
        }

        return new ExpectedAgenda(source, title, warnings, items, cer);
    }
}
