using System.Globalization;
using Memento.Documents.Agenda;

namespace Memento.Documents.Tests.Support;

/// <summary>One fixture's parse, with what the report shows.</summary>
internal sealed record FixtureOutcome(string Name, ExpectedAgenda Expected, AgendaParseResult? Result, Exception? Error, TimeSpan Elapsed)
{
    public IReadOnlyList<ExpectedItem> Actual =>
        Result?.Items.Select(i => new ExpectedItem(i.Text, i.Level, i.Uncertain, i.Time)).ToList() ?? [];

    /// <summary>The character error rate of the title and items together against the expected text (used for OCR).</summary>
    public double CharacterErrorRate =>
        Support.CharacterErrorRate.Of(Join(Expected.Title, Expected.Items.Select(i => i.Text)), Join(Result?.Title, Actual.Select(i => i.Text)));

    public string Summary
    {
        get
        {
            if (Result is null)
            {
                return $"{Name}: failed: {Error?.Message}";
            }

            var warnings = string.Join(", ", Result.Warnings.Select(w => w.Code));
            var cer = FixturePaths.IsImage(Name) ? string.Create(CultureInfo.InvariantCulture, $", CER {CharacterErrorRate:P1}") : string.Empty;
            return string.Create(
                CultureInfo.InvariantCulture,
                $"{Name}: {Result.Source}, {Result.Items.Count}/{Expected.Items.Count} items, {Result.UncertainCount} uncertain (expected {Expected.Items.Count(i => i.Uncertain)}), warnings [{warnings}]{cer}, {Elapsed.TotalMilliseconds:0} ms");
        }
    }

    private static string Join(string? title, IEnumerable<string> items) =>
        string.Join('\n', (title is null ? [] : new[] { title }).Concat(items));
}
