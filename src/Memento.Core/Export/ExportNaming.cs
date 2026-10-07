using System.Globalization;
using System.Text.RegularExpressions;

namespace Memento.Core.Export;

/// <summary>
/// Names of exported files and the export folder (BRIDGE.md M3 clarification 4, shared with the UI's
/// <c>exportFolderName</c>): characters Windows forbids become " - ", runs of spaces and dashes collapse, leading and
/// trailing spaces, dots and dashes go, the title is capped at 80 characters, then a space and the date
/// (the first 10 characters of <c>createdAt</c>). "Design review: library screen" → "Design review - library screen 2026-10-05".
/// </summary>
public static partial class ExportNaming
{
    public const string ManifestFile = "manifest.json";
    public const string AttachmentsFolder = "Attachments";
    public const int MaxTitleLength = 80;

    /// <summary>"Design review - library screen 2026-10-05": the subfolder name and the stem of every exported file.</summary>
    public static string BaseName(string title, DateTimeOffset createdAt)
    {
        var clean = Invalid().Replace(title ?? string.Empty, " - ");
        clean = Spaces().Replace(clean, " ");
        clean = Dashes().Replace(clean, " - ");
        clean = Edges().Replace(clean, string.Empty);
        if (clean.Length > MaxTitleLength)
        {
            clean = clean[..MaxTitleLength];
        }

        clean = clean.Trim();
        return (clean.Length == 0 ? "Recording" : clean) + " " + createdAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    public static string AudioExtension(string format) => "." + format;

    public static string TranscriptSuffix(string format) => format switch
    {
        ExportRules.Markdown => " - transcript.md",
        ExportRules.Text => " - transcript.txt",
        ExportRules.Srt => ".srt",
        _ => " - transcript.json",
    };

    // The names of the exported files (BRIDGE.md M3 integration clarification 16). The UI's exportFileNames builds
    // the same strings; ui/src/format/export-naming.cases.json holds the cases both sides are tested against.

    /// <summary>The mixed audio: <c>{base}.{format}</c>.</summary>
    public static string MixFile(string baseName, string format) => baseName + AudioExtension(format);

    /// <summary>One track: <c>{base} - {track name}.{format}</c>, the name made safe for Windows (the id when nothing is left).</summary>
    public static string TrackFile(string baseName, string trackName, string trackId, string format) =>
        $"{baseName} - {Attachments.FileNames.Sanitize(trackName, trackId)}{AudioExtension(format)}";

    /// <summary><c>{base} - transcript.json</c> / <c>.md</c> / <c>.txt</c>, and <c>{base}.srt</c>.</summary>
    public static string TranscriptFile(string baseName, string format) => baseName + TranscriptSuffix(format);

    /// <summary>The recording details: <c>{base} - details.json</c>.</summary>
    public static string DetailsFile(string baseName) => baseName + " - details.json";

    /// <summary>An attachment as the estimate and the manifest name it: <c>Attachments/{name}</c>.</summary>
    public static string AttachmentEntry(string name) => AttachmentsFolder + "/" + name;

    [GeneratedRegex("[<>:\"/\\\\|?*\\u0000-\\u001f]")]
    private static partial Regex Invalid();

    [GeneratedRegex("\\s+")]
    private static partial Regex Spaces();

    [GeneratedRegex("(?:\\s-\\s*){2,}")]
    private static partial Regex Dashes();

    [GeneratedRegex("^[\\s.-]+|[\\s.-]+$")]
    private static partial Regex Edges();
}
