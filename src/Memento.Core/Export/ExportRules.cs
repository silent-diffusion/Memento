using System.Globalization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Export;

/// <summary>The formats and limits of an <see cref="ExportSelection"/> (BRIDGE.md M3, DESIGN.md §15).</summary>
public static class ExportRules
{
    public const string Flac = "flac";
    public const string Wav = "wav";
    public const string Mp3 = "mp3";

    public const string Json = "json";
    public const string Markdown = "markdown";
    public const string Text = "text";
    public const string Srt = "srt";

    /// <summary>The Windows MP3 encoder's range (Memento.Audio <c>MediaFoundationLossyEncoder</c>).</summary>
    public const int MinMp3BitrateKbps = 96;
    public const int MaxMp3BitrateKbps = 320;
    public const int DefaultMp3BitrateKbps = 192;

    public static IReadOnlyList<string> AudioFormats { get; } = [Flac, Wav, Mp3];

    public static IReadOnlyList<string> TranscriptFormats { get; } = [Json, Markdown, Text, Srt];

    public static IReadOnlyList<string> DocumentFormats { get; } = ["docx", "pdf", "markdown"];

    /// <summary>2.0: <c>mp4</c> re-encodes (and can burn the camera inset in); <c>original</c> copies the recorded frames.</summary>
    public const string Mp4 = "mp4";
    public const string Original = "original";
    public const string OneFile = "oneFile";
    public const string TwoFiles = "twoFiles";

    public static IReadOnlyList<string> VideoFormats { get; } = [Mp4, Original];

    public static IReadOnlyList<string> VideoLayouts { get; } = [OneFile, TwoFiles];

    /// <summary>The AAC bitrate the mixed audio gets in a video export.</summary>
    public const int VideoAudioBitrateKbps = 192;

    /// <summary>Returns the first problem, worded for people, or <c>null</c>.</summary>
    public static string? Validate(ExportSelection? selection)
    {
        if (selection is null || selection.AudioMixed is null || selection.Tracks is null || selection.Transcript is null
            || selection.Documents is null || selection.Details is null || selection.Attachments is null)
        {
            return "The export selection needs every row: audioMixed, tracks, transcript, documents, details and attachments.";
        }

        return ValidateAudio(selection.AudioMixed, "Audio (mixed)")
            ?? ValidateAudio(selection.Tracks, "Individual tracks")
            ?? ValidateTranscript(selection.Transcript)
            ?? (DocumentFormats.Contains(selection.Documents.Format ?? string.Empty, StringComparer.Ordinal)
                ? null
                : $"Document format '{selection.Documents.Format}' is not available. Choose {string.Join(", ", DocumentFormats)}.")
            ?? ValidateVideo(selection.Video);
    }

    private static string? ValidateVideo(ExportVideoChoice? video)
    {
        if (video is null)
        {
            return null;
        }

        if (!VideoFormats.Contains(video.Format ?? string.Empty, StringComparer.Ordinal))
        {
            return $"Video: format '{video.Format}' is not available. Choose {string.Join(", ", VideoFormats)}.";
        }

        if (!VideoLayouts.Contains(video.Layout ?? string.Empty, StringComparer.Ordinal))
        {
            return $"Video: '{video.Layout}' is not a layout. Choose {string.Join(" or ", VideoLayouts)}.";
        }

        // Copying the recorded frames cannot burn the camera inset in, so "Original container" is for two files only.
        return video.On && video.Format == Original && video.Layout == OneFile
            ? "Video: \"Original container\" copies each track as it was recorded, so it writes two files; choose twoFiles, or MP4 for one file."
            : null;
    }

    /// <summary>The bitrate an MP3 export uses.</summary>
    public static int Mp3Bitrate(ExportAudioChoice choice) => choice.BitrateKbps ?? DefaultMp3BitrateKbps;

    private static string? ValidateAudio(ExportAudioChoice choice, string row)
    {
        if (!AudioFormats.Contains(choice.Format ?? string.Empty, StringComparer.Ordinal))
        {
            return $"{row}: format '{choice.Format}' is not available. Choose {string.Join(", ", AudioFormats)}.";
        }

        if (choice.Format == Mp3 && choice.BitrateKbps is { } kbps && kbps is < MinMp3BitrateKbps or > MaxMp3BitrateKbps)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{row}: MP3 needs a bitrate of {MinMp3BitrateKbps} to {MaxMp3BitrateKbps} kbps, not {kbps}.");
        }

        return null;
    }

    private static string? ValidateTranscript(ExportTranscriptChoice choice)
    {
        var formats = choice.Formats ?? [];
        var unknown = formats.FirstOrDefault(f => !TranscriptFormats.Contains(f ?? string.Empty, StringComparer.Ordinal));
        if (unknown is not null || formats.Count > TranscriptFormats.Count)
        {
            return $"Transcript format '{unknown}' is not available. Choose any of {string.Join(", ", TranscriptFormats)}.";
        }

        return TranscriptText.Validate(choice.Options);
    }
}
