using System.Globalization;
using System.Text;
using Memento.AI;
using Memento.AI.Payload;
using Memento.Core.Bridge.Contracts;

namespace Memento.Generation.Generation;

/// <summary>
/// "Preview what will be sent" as a sheet (2.0): every input of the checklist with whether it is ticked, whether its text
/// is in the payload and how large it is; what is held back or changed before sending; and each request of the run as
/// the provider will receive it (the map requests exactly, the verify request described, since its claims come from the
/// answers). Nothing here sends anything.
/// </summary>
public static class PayloadPreview
{
    /// <summary>The checklist's inputs in Builder order, with the payload sections each one becomes.</summary>
    private static readonly (string Key, string Label, PayloadSectionKind[] Kinds)[] Checklist =
    [
        ("transcript", "Transcript", [PayloadSectionKind.Transcript, PayloadSectionKind.Outline]),
        ("details", "Details", [PayloadSectionKind.Details]),
        ("participants", "Participants", [PayloadSectionKind.Participants]),
        ("agenda", "Agenda", [PayloadSectionKind.Agenda]),
        ("highlights", "Highlights and notes", [PayloadSectionKind.Highlights, PayloadSectionKind.Notes]),
        ("attachments", "Imported documents", [PayloadSectionKind.Attachments]),
        ("previousDocuments", "Earlier documents", [PayloadSectionKind.PreviousDocuments]),
    ];

    /// <param name="ticked">The template's ticks.</param>
    /// <param name="allowed">The ticks within what Settings shares with a cloud provider (the same as <paramref name="ticked"/> for the local model).</param>
    public static IReadOnlyList<PreviewInput> Inputs(PayloadSelection ticked, PayloadSelection allowed, ComposedPayload payload, bool cloud)
    {
        ArgumentNullException.ThrowIfNull(ticked);
        ArgumentNullException.ThrowIfNull(allowed);
        ArgumentNullException.ThrowIfNull(payload);
        var inputs = new List<PreviewInput>();
        foreach (var (key, label, kinds) in Checklist)
        {
            var sections = payload.Sections.Where(s => kinds.Contains(s.Kind)).ToList();
            var excluded = payload.Excluded.Where(e => kinds.Contains(e.Kind)).ToList();
            var isTicked = Ticked(ticked, key);
            string? note = null;
            if (isTicked && !Ticked(allowed, key))
            {
                note = cloud ? "Held back: Settings › AI and privacy does not share it with external services." : null;
            }
            else if (isTicked && sections.Count == 0)
            {
                note = excluded.Count > 0 ? string.Join(" ", excluded.Select(e => $"{e.Name}: {e.Reason}")) : "Nothing to send: this recording has none.";
            }
            else if (excluded.Count > 0)
            {
                note = "Left out: " + string.Join("; ", excluded.Select(e => $"{e.Name} ({e.Reason.TrimEnd('.')})")) + ".";
            }

            inputs.Add(Input(key, label, isTicked, sections, note));
        }

        var instructions = payload.Sections.Where(s => s.Kind == PayloadSectionKind.Instructions).ToList();
        if (instructions.Count > 0)
        {
            inputs.Add(Input("instructions", "The template's instructions", true, instructions, null));
        }

        return inputs;
    }

    /// <summary>What is held back or changed before sending, in sentences.</summary>
    public static IReadOnlyList<string> Redactions(IReadOnlyList<PreviewInput> inputs, ComposedPayload payload)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(payload);
        var redactions = new List<string>();
        redactions.AddRange(inputs.Where(i => i.Ticked && !i.Included && i.Note is { } note && note.StartsWith("Held back", StringComparison.Ordinal))
            .Select(i => $"{i.Label} is held back: Settings › AI and privacy does not share it with external services."));
        redactions.AddRange(payload.Excluded.Select(e => $"{e.Name} is left out: {e.Reason.TrimEnd('.')}."));
        if (payload.NeutralisedMarkers > 0)
        {
            redactions.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"Text shaped like a section marker was neutralised in {payload.NeutralisedMarkers} place{(payload.NeutralisedMarkers == 1 ? string.Empty : "s")} (‹ instead of <), so the recording's words cannot pass for instructions."));
        }

        return redactions;
    }

    /// <summary>The requests in pipeline order: every map request exactly, then the verify request described.</summary>
    public static IReadOnlyList<PreviewPass> Passes(IReadOnlyList<(OutputPass Pass, AiRequest Request)> map, IAiProvider provider, bool bounded)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(provider);
        var passes = new List<PreviewPass>();
        foreach (var (pass, request) in map)
        {
            passes.Add(Pass(passes.Count + 1, pass.Step, pass.Title, request, provider, exact: true));
        }

        if (passes.Count > 0)
        {
            var verify = VerifyPrompts.BatchOf(
                [("‹a statement from the answers above›", "‹the line it cites, with two lines either side, as in the transcript›")],
                bounded);
            passes.Add(Pass(passes.Count + 1, GenerationOutputFeed.VerifyStep, "Check each claim · built from the answers", verify, provider, exact: false));
        }

        return passes;
    }

    /// <summary>The whole preview, or one pass, as the text the clipboard gets.</summary>
    public static string Text(GenerationPreviewResult preview, string? passId)
    {
        ArgumentNullException.ThrowIfNull(preview);
        var passes = passId is null ? preview.Passes : preview.Passes.Where(p => p.Id == passId).ToList();
        var text = new StringBuilder();
        if (passId is null)
        {
            var provider = preview.Provider;
            text.Append(preview.StaysOnPc ? "Read by " : "Sent to ").Append(provider?.Name ?? preview.ProviderId)
                .Append(provider?.ModelLabel is { } model ? $" ({model})" : string.Empty).Append('\n');
            text.Append("Inputs: ").Append(string.Join("; ", preview.Inputs.Where(i => i.Included).Select(i => $"{i.Label} ({i.Characters.ToString("N0", CultureInfo.InvariantCulture)} characters)"))).Append('\n');
            foreach (var redaction in preview.Redactions)
            {
                text.Append("- ").Append(redaction).Append('\n');
            }

            text.Append("Audio and video are never sent.\n\n");
        }

        foreach (var pass in passes)
        {
            text.Append("=== ").Append(pass.Title).Append(pass.Exact ? string.Empty : " (illustration)").Append(" ===\n");
            text.Append(AiRequestText.SystemHeading).Append('\n').Append(pass.System).Append("\n\n")
                .Append(AiRequestText.UserHeading).Append('\n').Append(pass.User).Append("\n\n");
        }

        return text.ToString().TrimEnd('\n') + "\n";
    }

    private static PreviewPass Pass(int number, string step, string title, AiRequest request, IAiProvider provider, bool exact)
    {
        var rendered = AiRequestText.Render(request);
        var user = string.Join("\n\n", request.Messages.Select(m => m.Content));
        return new PreviewPass(
            "p" + number.ToString(CultureInfo.InvariantCulture),
            step,
            title,
            request.System,
            user,
            request.System.Length + user.Length,
            Encoding.UTF8.GetByteCount(request.System) + Encoding.UTF8.GetByteCount(user),
            provider.CountTokens(rendered),
            exact);
    }

    private static PreviewInput Input(string key, string label, bool ticked, List<PayloadSection> sections, string? note) => new(
        key,
        label,
        ticked,
        sections.Count > 0,
        sections.Sum(s => (long)s.Text.Length),
        sections.Sum(s => (long)Encoding.UTF8.GetByteCount(s.Text)),
        sections.Count == 0 ? null : string.Join("; ", sections.Select(s => s.Summary).Where(s => s.Length > 0)) is { Length: > 0 } summary ? summary : null,
        note);

    private static bool Ticked(PayloadSelection selection, string key) => key switch
    {
        "transcript" => selection.Transcript,
        "details" => selection.Details,
        "participants" => selection.Participants,
        "agenda" => selection.Agenda,
        "highlights" => selection.Highlights,
        "attachments" => selection.Attachments,
        "previousDocuments" => selection.PreviousDocuments,
        _ => false,
    };
}
