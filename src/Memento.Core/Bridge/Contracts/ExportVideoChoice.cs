namespace Memento.Core.Bridge.Contracts;

/// <summary>The Export dialog's Video row (DESIGN.md §18.4).</summary>
public sealed record ExportVideoChoice
{
    public bool On { get; init; }

    /// <summary><c>mp4</c> (re-encodes; can burn the camera inset in) or <c>original</c> (copies the recorded frames untouched).</summary>
    public string Format { get; set; } = "mp4";

    /// <summary>
    /// <c>oneFile</c>: the screen with the camera inset where it was while recording, and the mixed audio. <c>twoFiles</c>:
    /// screen and camera at their own resolution; the mixed audio goes with the screen file.
    /// </summary>
    public string Layout { get; set; } = "oneFile";
}
