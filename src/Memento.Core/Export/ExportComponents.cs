namespace Memento.Core.Export;

/// <summary>The rows of the Export dialog: the keys of <c>ExportSelection</c>.</summary>
public static class ExportComponents
{
    public const string AudioMixed = "audioMixed";
    public const string Tracks = "tracks";
    public const string Transcript = "transcript";
    public const string Documents = "documents";
    public const string Details = "details";
    public const string Attachments = "attachments";

    /// <summary>The row's name in the dialog, for messages.</summary>
    public static string Label(string component) => component switch
    {
        AudioMixed => "Audio (mixed)",
        Tracks => "Individual tracks",
        Transcript => "Transcript",
        Documents => "Documents",
        Details => "Recording details",
        _ => "Attachments",
    };
}
