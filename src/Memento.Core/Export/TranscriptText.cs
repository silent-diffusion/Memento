using System.Globalization;
using System.Text;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Transcripts;

namespace Memento.Core.Export;

/// <summary>
/// The readable transcript exports (BRIDGE.md M3): Markdown with speaker-labelled paragraphs and an <c>[h:mm:ss]</c>
/// marker before every segment, and plain text with one line per segment. Line endings are CRLF, for Windows apps.
/// </summary>
public static class TranscriptText
{
    public const string NewLine = "\r\n";

    /// <summary><c>[h:mm:ss]</c>, hours always shown: <c>[0:00:12]</c>, <c>[1:02:03]</c>.</summary>
    public static string Marker(double seconds)
    {
        var time = TimeSpan.FromSeconds(Math.Max(0, Math.Floor(seconds)));
        return string.Create(CultureInfo.InvariantCulture, $"[{(int)time.TotalHours}:{time.Minutes:00}:{time.Seconds:00}]");
    }

    public static string Markdown(TranscriptDocument transcript, RecordingSummary summary)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(summary);
        var names = SpeakerNames(transcript);
        var text = new StringBuilder();
        text.Append("# ").Append(EscapeMarkdown(summary.Title)).Append(NewLine).Append(NewLine);
        text.Append(Heading(summary, transcript, names)).Append(NewLine);

        string? paragraphSpeaker = null;
        var open = false;
        foreach (var segment in transcript.Segments)
        {
            var body = Clean(segment.Text);
            if (body.Length == 0)
            {
                continue;
            }

            var speaker = segment.Speaker is { } id ? names.GetValueOrDefault(id, id) : null;
            if (!open || speaker is null || speaker != paragraphSpeaker)
            {
                text.Append(NewLine);
                if (open)
                {
                    text.Append(NewLine);
                }

                if (speaker is not null)
                {
                    text.Append("**").Append(EscapeMarkdown(speaker)).Append(":** ");
                }

                open = true;
                paragraphSpeaker = speaker;
            }
            else
            {
                text.Append(' ');
            }

            text.Append(Marker(segment.Start)).Append(' ').Append(EscapeMarkdown(body));
        }

        if (open)
        {
            text.Append(NewLine);
        }

        return text.ToString();
    }

    public static string Plain(TranscriptDocument transcript, RecordingSummary summary)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(summary);
        var names = SpeakerNames(transcript);
        var text = new StringBuilder();
        text.Append(summary.Title).Append(NewLine);
        text.Append(Heading(summary, transcript, names)).Append(NewLine);
        foreach (var segment in transcript.Segments)
        {
            var body = Clean(segment.Text);
            if (body.Length == 0)
            {
                continue;
            }

            text.Append(Marker(segment.Start)).Append(' ');
            if (segment.Speaker is { } id)
            {
                text.Append(names.GetValueOrDefault(id, id)).Append(": ");
            }

            text.Append(body).Append(NewLine);
        }

        return text.ToString();
    }

    /// <summary>Speaker id → name as the transcript shows it.</summary>
    public static Dictionary<string, string> SpeakerNames(TranscriptDocument transcript) =>
        transcript.Speakers.GroupBy(s => s.Id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First().Name, StringComparer.Ordinal);

    /// <summary>Segment text on one line, spaces collapsed.</summary>
    public static string Clean(string? text) =>
        string.Join(' ', (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Escapes the characters Markdown would read as formatting.</summary>
    public static string EscapeMarkdown(string text)
    {
        var builder = new StringBuilder(text.Length + 8);
        foreach (var c in text)
        {
            if (c is '\\' or '*' or '_' or '`' or '[' or ']' or '<' or '>' or '#' or '|' or '~')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static string Heading(RecordingSummary summary, TranscriptDocument transcript, Dictionary<string, string> names)
    {
        var parts = new List<string>
        {
            summary.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            HumanFormat.Clock(summary.DurationMs),
        };
        var speakers = transcript.Segments.Select(s => s.Speaker).OfType<string>().Distinct(StringComparer.Ordinal)
            .Select(id => names.GetValueOrDefault(id, id)).ToList();
        if (speakers.Count > 0)
        {
            parts.Add("Speakers: " + string.Join(", ", speakers));
        }

        return string.Join(" · ", parts);
    }
}
