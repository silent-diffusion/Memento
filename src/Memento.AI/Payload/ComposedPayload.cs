using System.Globalization;
using System.Text;

namespace Memento.AI.Payload;

/// <summary>
/// The exact payload for the ticked inputs: its sections, what was left out and why, the transcript lines (for
/// chunking and for resolving short-id citations), the full text and its SHA-256.
/// </summary>
/// <param name="Text">Every section, in order, separated by a blank line: exactly what a single request would carry.</param>
/// <param name="Hash">Lower-case hex SHA-256 of <see cref="Text"/> (UTF-8).</param>
/// <param name="NeutralisedMarkers">
/// Places where input text contained something shaped like a section tag (<c>&lt;/transcript&gt;</c>), rewritten
/// with ‹ so the text cannot close a section early; shown in the preview.
/// </param>
public sealed record ComposedPayload(
    IReadOnlyList<PayloadSection> Sections,
    IReadOnlyList<PayloadExclusion> Excluded,
    IReadOnlyList<TranscriptLine> TranscriptLines,
    string Text,
    string Hash,
    int NeutralisedMarkers)
{
    /// <summary>Every section except the transcript (the context a per-chunk request carries).</summary>
    public string ContextText => string.Join("\n\n", Sections.Where(s => s.Kind != PayloadSectionKind.Transcript).Select(s => s.Text));

    /// <summary>The transcript line a citation's short id points to (the first part of a split segment).</summary>
    public TranscriptLine? Line(int shortId) => TranscriptLines.FirstOrDefault(l => l.ShortId == shortId);

    /// <summary>
    /// "Preview exactly what will be sent": what is included and left out, the hard rule on audio and video, the size
    /// and the hash, then the payload text itself, unchanged.
    /// </summary>
    public string RenderPreview(IAiProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        var builder = new StringBuilder();
        builder.AppendLine(provider.Kind == AiProviderKind.Local
            ? "Exactly what the local model will read (nothing leaves this PC)"
            : $"Exactly what will be sent to {provider.DisplayName}");
        builder.Append("Included: ")
            .AppendLine(Sections.Count == 0 ? "nothing" : string.Join("; ", Sections.Select(s => s.Summary.Length == 0 ? s.Title : $"{s.Title} ({s.Summary})")));
        if (Excluded.Count > 0)
        {
            builder.AppendLine("Not included:");
            foreach (var item in Excluded)
            {
                builder.Append("- ").Append(item.Name).Append(": ").AppendLine(item.Reason);
            }
        }

        builder.AppendLine("Audio and video are never sent.");
        if (NeutralisedMarkers > 0)
        {
            builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Text shaped like a section marker was neutralised in {NeutralisedMarkers} place(s): ‹ instead of <."));
        }

        builder.AppendLine(string.Create(CultureInfo.InvariantCulture, $"Size: about {provider.CountTokens(Text):N0} tokens. SHA-256: {Hash}"));
        builder.AppendLine("--- payload ---");
        builder.Append(Text);
        if (Text.Length > 0 && Text[^1] != '\n')
        {
            builder.Append('\n');
        }

        builder.Append("--- end of payload ---");
        return builder.ToString();
    }
}
