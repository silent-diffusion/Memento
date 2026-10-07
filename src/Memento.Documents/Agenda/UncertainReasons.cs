using System.Globalization;

namespace Memento.Documents.Agenda;

/// <summary>
/// The user-facing reasons an item is marked uncertain (DESIGN.md §14 and §17: say what may be wrong, what was kept,
/// and how to fix it here).
/// </summary>
public static class UncertainReasons
{
    public const string HeadingMerged =
        "A heading and its first bullet may have been merged. Check the wording, or split it into two items here.";

    public const string SeveralItemsMerged =
        "Several items may have been merged into this line. Split them here if they are separate topics.";

    public const string TooShort =
        "This item is very short and may be a stray mark from the document. Remove it here if it is not an agenda item.";

    public static string NumberedItemsMerged(string next) =>
        $"Item {next} may be on the same line as this one. Split them here if they are separate.";

    public static string NumberingSkips(string from, string to) =>
        $"The numbering jumps from {from} to {to} here, so an item may be missing. Compare with the original and add it here if needed.";

    public static string NumberingStartsLate(string first) =>
        $"The numbering starts at {first} here, so an earlier item may be missing. Compare with the original and add it here if needed.";

    public static string NumberingRepeats(string number) =>
        $"The number {number} is used twice here. Both items were kept; remove one if it is a duplicate.";

    public static string NumberingGoesBack(string from, string to) =>
        $"The numbering goes back from {from} to {to} here, so items may be out of order. Reorder them here if needed.";

    public static string TooLong(int length) =>
        string.Create(CultureInfo.InvariantCulture, $"This item is unusually long ({length} characters) and may be several items run together. Shorten or split it here.");

    public static string OcrSuspicious(string token) =>
        $"Text recognition may have misread \"{token}\". Compare it with the original and correct it here.";

    public static string OcrLowConfidence(string word) =>
        $"Text recognition was unsure about \"{word}\". Compare it with the original and correct it here.";

    public static string ColumnAmbiguous(string used, string other) =>
        $"The table has more than one column that could hold the agenda; \"{used}\" was used, not \"{other}\". Edit the items here if the other column was meant.";

    public static string ColumnGuessed(string used) =>
        $"The table has no header row, so column {used} was taken as the agenda. Edit the items here if another column was meant.";
}
