using System.Text;

namespace Memento.Documents.Agenda.Tables;

/// <summary>An RFC 4180 reader: quoted fields, doubled quotes, line breaks inside quotes; picks , ; or tab.</summary>
internal static class DelimitedReader
{
    public static char DetectDelimiter(string text, bool preferTab)
    {
        if (preferTab)
        {
            return '\t';
        }

        // Count each candidate outside quotes on the first lines; the one that appears consistently wins.
        var lines = text.Split('\n').Where(l => l.Trim().Length > 0).Take(10).ToList();
        char[] candidates = [',', ';', '\t'];
        var best = ',';
        var bestScore = -1;
        foreach (var candidate in candidates)
        {
            var counts = lines.Select(l => CountOutsideQuotes(l, candidate)).ToList();
            if (counts.Count == 0 || counts.Max() == 0)
            {
                continue;
            }

            var consistent = counts.Count(c => c == counts[0] && c > 0);
            var score = consistent * 100 + counts.Sum();
            if (score > bestScore)
            {
                best = candidate;
                bestScore = score;
            }
        }

        return best;
    }

    /// <summary>
    /// The rows of <paramref name="text"/>, at most <see cref="AgendaTableReader.MaxRows"/> of them with at most
    /// <see cref="AgendaTableReader.MaxColumns"/> fields each; <paramref name="truncated"/> says whether anything was left out.
    /// </summary>
    public static List<List<string>> Read(string text, char delimiter, CancellationToken cancellationToken, out bool truncated)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;
        var cut = false;

        void EndField()
        {
            if (row.Count < AgendaTableReader.MaxColumns)
            {
                row.Add(field.ToString());
            }
            else
            {
                cut = true;
            }

            field.Clear();
            fieldStarted = false;
        }

        for (var i = 0; i < text.Length; i++)
        {
            if (rows.Count >= AgendaTableReader.MaxRows)
            {
                cut = true;
                truncated = cut;
                return rows;
            }

            if ((i & 4095) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var c = text[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"')
                    {
                        field.Append('"');
                        i++;
                    }
                    else
                    {
                        quoted = false;
                    }
                }
                else
                {
                    field.Append(c);
                }

                continue;
            }

            if (c == '"' && !fieldStarted)
            {
                quoted = true;
                fieldStarted = true;
            }
            else if (c == delimiter)
            {
                EndField();
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                EndField();
                rows.Add(row);
                row = [];
            }
            else
            {
                field.Append(c);
                fieldStarted = true;
            }
        }

        if (fieldStarted || field.Length > 0 || row.Count > 0)
        {
            if (rows.Count >= AgendaTableReader.MaxRows)
            {
                cut = true;
                truncated = cut;
                return rows;
            }

            EndField();
            rows.Add(row);
        }

        truncated = cut;
        return rows;
    }

    private static int CountOutsideQuotes(string line, char candidate)
    {
        var count = 0;
        var quoted = false;
        foreach (var c in line)
        {
            if (c == '"')
            {
                quoted = !quoted;
            }
            else if (c == candidate && !quoted)
            {
                count++;
            }
        }

        return count;
    }
}
