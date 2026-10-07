namespace Memento.Documents.Agenda.Text;

/// <summary>Recognizes the shape of text that has no file name to go by (pasted text, a text file with the wrong extension).</summary>
internal static class TextShapes
{
    /// <summary>
    /// Cells copied from a spreadsheet: at least two lines, most of them with the same number of tabs between words
    /// (not just tabs used for indentation).
    /// </summary>
    public static bool LooksLikeTabTable(string text)
    {
        var lines = TextLines.Split(text).Where(l => l.Trim().Length > 0).Take(200).ToList();
        if (lines.Count < 2)
        {
            return false;
        }

        var counts = lines.Select(l => l.Trim().Count(c => c == '\t')).ToList();
        var withTabs = counts.Count(c => c > 0);
        if (withTabs * 10 < lines.Count * 8)
        {
            return false;
        }

        var common = counts.Where(c => c > 0).GroupBy(c => c).OrderByDescending(g => g.Count()).First();
        return common.Count() * 10 >= lines.Count * 7;
    }
}
