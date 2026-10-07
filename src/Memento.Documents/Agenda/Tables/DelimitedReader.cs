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

    public static List<List<string>> Read(string text, char delimiter, CancellationToken cancellationToken)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var quoted = false;
        var fieldStarted = false;
        for (var i = 0; i < text.Length; i++)
        {
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
                row.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
            }
            else if (c is '\r' or '\n')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                fieldStarted = false;
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
            row.Add(field.ToString());
            rows.Add(row);
        }

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
