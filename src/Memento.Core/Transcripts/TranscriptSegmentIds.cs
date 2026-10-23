using System.Globalization;

namespace Memento.Core.Transcripts;

/// <summary>
/// Ids of lines made out of another line: the first part keeps the line's id, the later ones are <c>{id}-2</c>,
/// <c>{id}-3</c>… (the speakers stage's line splitting and <c>transcript.splitSegment</c>), so a reference to the original
/// still lands on its first words and no two lines ever share an id.
/// </summary>
public static class TranscriptSegmentIds
{
    /// <summary>
    /// <c>{id}-{number}</c>, counting on past any id in <paramref name="taken"/>, and added to it so no later part takes it.
    /// </summary>
    public static string Child(string id, int number, ISet<string> taken)
    {
        ArgumentNullException.ThrowIfNull(id);
        ArgumentNullException.ThrowIfNull(taken);
        var candidate = string.Create(CultureInfo.InvariantCulture, $"{id}-{number}");
        while (!taken.Add(candidate))
        {
            number++;
            candidate = string.Create(CultureInfo.InvariantCulture, $"{id}-{number}");
        }

        return candidate;
    }
}
