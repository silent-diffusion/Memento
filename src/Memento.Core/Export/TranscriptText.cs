using System.Globalization;
using System.Text;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Formatting;
using Memento.Core.Transcripts;

namespace Memento.Core.Export;

/// <summary>
/// The readable transcript (BRIDGE.md M3, and "Clipboard and transcript text options"): Markdown and plain text, for
/// export files and the clipboard. One formatter for both, so a copy reads exactly like the file. The
/// <see cref="TranscriptTextOptions"/> choose timestamps, speakers and the layout in any combination; the defaults are
/// Markdown with speaker-labelled paragraphs and an <c>[h:mm:ss]</c> marker before every segment, and plain text with
/// one line per segment. Line endings are CRLF, for Windows apps.
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

    /// <summary>The layout <paramref name="options"/> gives <paramref name="format"/>, with <c>auto</c> resolved per format.</summary>
    public static string LayoutFor(string format, TranscriptTextOptions? options)
    {
        var layout = (options ?? TranscriptTextOptions.Default).Layout;
        if (layout is TranscriptTextOptions.Turns or TranscriptTextOptions.Lines)
        {
            return layout;
        }

        return format == ExportRules.Markdown ? TranscriptTextOptions.Turns : TranscriptTextOptions.Lines;
    }

    /// <summary>Returns the first problem with <paramref name="options"/>, worded for people, or <c>null</c>.</summary>
    public static string? Validate(TranscriptTextOptions? options) =>
        options is null || TranscriptTextOptions.Layouts.Contains(options.Layout ?? string.Empty, StringComparer.Ordinal)
            ? null
            : $"Transcript layout '{options.Layout}' is not available. Choose {string.Join(", ", TranscriptTextOptions.Layouts)}.";

    /// <summary><see cref="Markdown"/> or <see cref="Plain"/> by export format name (<c>markdown</c>, <c>text</c>).</summary>
    public static string Format(string format, TranscriptDocument transcript, RecordingSummary summary, TranscriptTextOptions? options = null, IReadOnlySet<string>? only = null) =>
        format switch
        {
            ExportRules.Markdown => Markdown(transcript, summary, options, only),
            ExportRules.Text => Plain(transcript, summary, options, only),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Only markdown and text are readable transcript formats."),
        };

    /// <param name="only">The segment ids to write (a filtered view in Review); <c>null</c> writes them all.</param>
    public static string Markdown(TranscriptDocument transcript, RecordingSummary summary, TranscriptTextOptions? options = null, IReadOnlySet<string>? only = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(summary);
        var choice = options ?? TranscriptTextOptions.Default;
        var lines = Lines(transcript, choice, only);
        var text = new StringBuilder();
        text.Append("# ").Append(EscapeMarkdown(summary.Title)).Append(NewLine).Append(NewLine);
        text.Append(Heading(summary, lines, choice)).Append(NewLine);

        var paragraphs = new List<string>();
        foreach (var group in Group(lines, LayoutFor(ExportRules.Markdown, choice)))
        {
            var paragraph = new StringBuilder();
            if (group[0].Speaker is { } speaker)
            {
                paragraph.Append("**").Append(EscapeMarkdown(speaker)).Append(":** ");
            }

            paragraph.AppendJoin(' ', group.Select(line => (line.Marker is null ? string.Empty : line.Marker + " ") + EscapeMarkdown(line.Body)));
            paragraphs.Add(paragraph.ToString());
        }

        if (paragraphs.Count > 0)
        {
            text.Append(NewLine).AppendJoin(NewLine + NewLine, paragraphs).Append(NewLine);
        }

        return text.ToString();
    }

    /// <param name="only">The segment ids to write (a filtered view in Review); <c>null</c> writes them all.</param>
    public static string Plain(TranscriptDocument transcript, RecordingSummary summary, TranscriptTextOptions? options = null, IReadOnlySet<string>? only = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(summary);
        var choice = options ?? TranscriptTextOptions.Default;
        var lines = Lines(transcript, choice, only);
        var text = new StringBuilder();
        text.Append(summary.Title).Append(NewLine);
        text.Append(Heading(summary, lines, choice)).Append(NewLine);
        if (LayoutFor(ExportRules.Text, choice) == TranscriptTextOptions.Lines)
        {
            // A line per segment, as Memento has always written it: "[0:00:12] Speaker 1: text".
            foreach (var line in lines)
            {
                if (line.Marker is not null)
                {
                    text.Append(line.Marker).Append(' ');
                }

                if (line.Speaker is not null)
                {
                    text.Append(line.Speaker).Append(": ");
                }

                text.Append(line.Body).Append(NewLine);
            }

            return text.ToString();
        }

        // A paragraph per speaker turn, a blank line before each: "Speaker 1: [0:00:12] text [0:00:15] text".
        foreach (var group in Group(lines, TranscriptTextOptions.Turns))
        {
            text.Append(NewLine);
            if (group[0].Speaker is { } speaker)
            {
                text.Append(speaker).Append(": ");
            }

            text.AppendJoin(' ', group.Select(line => (line.Marker is null ? string.Empty : line.Marker + " ") + line.Body)).Append(NewLine);
        }

        return text.ToString();
    }

    /// <summary>The segments a readable transcript writes: in order, with words, limited to <paramref name="only"/>.</summary>
    public static IReadOnlyList<TranscriptSegment> Selected(TranscriptDocument transcript, IReadOnlySet<string>? only = null)
    {
        ArgumentNullException.ThrowIfNull(transcript);
        return transcript.Segments.Where(s => Clean(s.Text).Length > 0 && (only is null || only.Contains(s.Id))).ToList();
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

    private static List<Line> Lines(TranscriptDocument transcript, TranscriptTextOptions options, IReadOnlySet<string>? only)
    {
        var names = SpeakerNames(transcript);
        return Selected(transcript, only)
            .Select(s => new Line(
                s.Speaker,
                options.Speakers && s.Speaker is { } id ? names.GetValueOrDefault(id, id) : null,
                options.Timestamps ? Marker(s.Start) : null,
                Clean(s.Text)))
            .ToList();
    }

    /// <summary>
    /// Runs of lines that share a paragraph: consecutive lines of the same speaker for <c>turns</c> (a line without a
    /// speaker stands alone), every line on its own for <c>lines</c>.
    /// </summary>
    private static IEnumerable<List<Line>> Group(List<Line> lines, string layout)
    {
        List<Line>? current = null;
        foreach (var line in lines)
        {
            var joins = layout == TranscriptTextOptions.Turns && current is not null && line.SpeakerId is not null
                && string.Equals(current[^1].SpeakerId, line.SpeakerId, StringComparison.Ordinal);
            if (joins)
            {
                current!.Add(line);
                continue;
            }

            if (current is not null)
            {
                yield return current;
            }

            current = [line];
        }

        if (current is not null)
        {
            yield return current;
        }
    }

    private static string Heading(RecordingSummary summary, List<Line> lines, TranscriptTextOptions options)
    {
        var parts = new List<string>
        {
            summary.CreatedAt.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture),
            HumanFormat.Clock(summary.DurationMs),
        };
        if (options.Speakers)
        {
            var speakers = lines.Select(l => l.Speaker).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
            if (speakers.Count > 0)
            {
                parts.Add("Speakers: " + string.Join(", ", speakers));
            }
        }

        return string.Join(" · ", parts);
    }

    /// <summary>One segment as the readable formats write it.</summary>
    /// <param name="SpeakerId">Groups turns even when names are left out; <c>null</c> for a line without a speaker.</param>
    /// <param name="Speaker">The name to write, or <c>null</c> (no speaker, or speakers left out).</param>
    /// <param name="Marker">The <c>[h:mm:ss]</c> marker, or <c>null</c> when timestamps are left out.</param>
    /// <param name="Body">The segment's text on one line.</param>
    private sealed record Line(string? SpeakerId, string? Speaker, string? Marker, string Body);
}
