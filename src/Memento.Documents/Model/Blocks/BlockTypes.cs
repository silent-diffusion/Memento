namespace Memento.Documents.Model.Blocks;

/// <summary>The <c>type</c> values of <see cref="Block"/>.</summary>
public static class BlockTypes
{
    public const string Heading = "heading";
    public const string Paragraph = "paragraph";
    public const string List = "list";
    public const string Table = "table";
    public const string Chips = "chips";
    public const string LabelValue = "labelValue";
    public const string Quote = "quote";
    public const string Timeline = "timeline";
    public const string Transcript = "transcript";

    public static IReadOnlyList<string> All { get; } = [Heading, Paragraph, List, Table, Chips, LabelValue, Quote, Timeline, Transcript];
}
