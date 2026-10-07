using System.Globalization;
using Memento.Core.Attachments;

namespace Memento.Core.Export;

/// <summary>Names of exported files: the recording's title made safe for Windows plus its date (BRIDGE.md M3).</summary>
public static class ExportNaming
{
    public const string ManifestFile = "manifest.json";
    public const string AttachmentsFolder = "Attachments";

    /// <summary>"Weekly sync 2026-10-06".</summary>
    public static string BaseName(string title, DateTimeOffset createdAt) =>
        FileNames.Sanitize(title, "Recording") + " " + createdAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string AudioExtension(string format) => "." + format;

    public static string TranscriptSuffix(string format) => format switch
    {
        ExportRules.Markdown => " - transcript.md",
        ExportRules.Text => " - transcript.txt",
        ExportRules.Srt => ".srt",
        _ => " - transcript.json",
    };
}
