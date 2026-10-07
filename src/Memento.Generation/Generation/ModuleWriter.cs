using System.Globalization;
using Memento.Core.Bridge.Contracts;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Model.Modules;
using Memento.Documents.Templates;

namespace Memento.Generation.Generation;

/// <summary>
/// Writes a module's kept claims into its content shape (paragraph, list, table, label/value, quote, timeline) within its
/// length setting, with a timestamp after each statement when the module links to the transcript. What the recording
/// does not hold is said in the spec's words: "Not discussed.", "Not reached", "No owner named", "No date given".
/// </summary>
public static class ModuleWriter
{
    public const string NotDiscussed = "Not discussed.";
    public const string NotReached = "Not reached";
    public const string NoOwner = "No owner named";
    public const string NoDate = "No date given";

    /// <param name="claims">The module's kept claims, in time order.</param>
    /// <param name="agenda">The agenda items (for the Agenda module), with the coverage the user marked.</param>
    public static IReadOnlyList<Block> Write(
        TemplateModule module,
        ModuleDefinition definition,
        IReadOnlyList<Claim> claims,
        TranscriptIndex transcript,
        IReadOnlyList<AgendaItem> agenda,
        GenerationFacts facts)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(transcript);
        ArgumentNullException.ThrowIfNull(agenda);
        ArgumentNullException.ThrowIfNull(facts);
        var link = module.LinkToTranscript;
        double? T(Claim c) => transcript.Find(c.Line)?.Start;
        IEnumerable<Run> Stamp(Claim c) => link && T(c) is { } t ? [Run.Plain(" "), Run.Timestamp(t)] : [];

        switch (module.Type)
        {
            case ModuleIds.Agenda:
                return agenda.Count == 0 ? [Note("No agenda was provided.")] : [Agenda(agenda, claims, transcript, link, facts.AgendaChecked)];
            case ModuleIds.Decisions:
            case ModuleIds.OpenQuestions:
                return claims.Count == 0
                    ? [Note(NotDiscussed)]
                    : [new ListBlock { Items = claims.Select(c => new ListItem { Runs = [Run.Plain(Sentence(c.Text)), .. Stamp(c)] }).ToList() }];
            case ModuleIds.ActionItems:
                return claims.Count == 0 ? [Note("No commitments were made.")] : [ActionTable(definition, claims, Stamp)];
            case ModuleIds.Deadline:
                var dated = claims.Where(c => c.Due is not null).ToList();
                return dated.Count == 0
                    ? [Note("No deadlines were stated.")]
                    : [new TableBlock
                    {
                        Columns = definition.Columns.Count == 2 ? definition.Columns : ["Due", "Action"],
                        Widths = definition.ColumnWidths,
                        Rows = dated.Select(c => new TableRow { Cells = [TableCell.Of(c.Due!), TableCell.Of([Run.Plain(Sentence(c.Text)), .. Stamp(c)])] }).ToList(),
                    }];
            case ModuleIds.Owner:
                var owned = claims.Where(c => c.Owner is not null).GroupBy(c => c.Owner!, StringComparer.OrdinalIgnoreCase).ToList();
                return owned.Count == 0
                    ? [Note("No owner was named for any task.")]
                    : [new LabelValueBlock
                    {
                        Pairs = owned.Select(g => new LabelValuePair
                        {
                            Label = g.Key,
                            Runs = g.SelectMany((c, i) => (IEnumerable<Run>)[.. (i > 0 ? new[] { Run.Plain("; ") } : []), Run.Plain(Sentence(c.Text).TrimEnd('.')), .. Stamp(c)]).ToList(),
                        }).ToList(),
                    }];
            case ModuleIds.Quote:
                return claims.Count == 0
                    ? [Note(NotDiscussed)]
                    : claims.Select(c => (Block)new QuoteBlock { Runs = [Run.Plain(c.Quote ?? c.Text)], Attribution = transcript.Find(c.Line)?.Speaker, T = link ? T(c) : null }).ToList();
            case ModuleIds.Timeline:
                return claims.Count == 0
                    ? [Note(NotDiscussed)]
                    : [new TimelineBlock { Entries = claims.Where(c => T(c) is not null).Select(c => new TimelineEntry { T = T(c)!.Value, Runs = [Run.Plain(Sentence(c.Text))] }).ToList() }];
            case ModuleIds.MeetingPurpose:
                return [new LabelValueBlock
                {
                    Pairs =
                    [
                        new LabelValuePair
                        {
                            Label = definition.Labels.Count > 0 ? definition.Labels[0] : "Purpose",
                            Runs = facts.Purpose is { } purpose ? [Run.Plain(purpose)]
                                : claims.Count == 0 ? [Run.Note(NotDiscussed)]
                                : [Run.Plain(Sentence(claims[0].Text)), .. Stamp(claims[0])],
                        },
                    ],
                }];
            case ModuleIds.NextMeeting:
                var labels = definition.Labels.Count >= 2 ? definition.Labels : ["When", "Agenda"];
                var when = claims.Where(c => c.Kind == ClaimKinds.When).ToList();
                var topics = claims.Where(c => c.Kind == ClaimKinds.NextAgenda).ToList();
                return [new LabelValueBlock
                {
                    Pairs =
                    [
                        new LabelValuePair { Label = labels[0], Runs = when.Count == 0 ? [Run.Note(NotDiscussed)] : [Run.Plain(Sentence(when[0].Text)), .. Stamp(when[0])] },
                        new LabelValuePair { Label = labels[1], Runs = topics.Count == 0 ? [Run.Note(NotDiscussed)] : topics.SelectMany((c, i) => (IEnumerable<Run>)[.. (i > 0 ? new[] { Run.Plain("; ") } : []), Run.Plain(c.Text.TrimEnd('.')), .. Stamp(c)]).ToList() },
                    ],
                }];
            case ModuleIds.FollowUpEmail:
                return FollowUpEmail(claims, facts, Stamp);
            default:
                if (claims.Count == 0)
                {
                    return [Note(NotDiscussed)];
                }

                // Paragraph modules: the statements in time order; long ones in paragraphs of three.
                var perParagraph = definition.DefaultLength == ModuleLength.Long || module.Length == ModuleLength.Long ? 3 : int.MaxValue;
                return claims.Chunk(perParagraph)
                    .Select(group => (Block)new ParagraphBlock { Runs = group.SelectMany((c, i) => (IEnumerable<Run>)[.. (i > 0 ? new[] { Run.Plain(" ") } : []), Run.Plain(Sentence(c.Text)), .. Stamp(c)]).ToList() })
                    .ToList();
        }
    }

    public static ParagraphBlock Note(string text) => new() { Runs = [Run.Note(text)] };

    /// <summary>A statement as a sentence: trimmed, capitalised, ending with a full stop.</summary>
    public static string Sentence(string text)
    {
        var value = (text ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return value;
        }

        value = char.ToUpper(value[0], CultureInfo.InvariantCulture) + value[1..];
        return value[^1] is '.' or '!' or '?' ? value : value + ".";
    }

    private static TableBlock ActionTable(ModuleDefinition definition, IReadOnlyList<Claim> claims, Func<Claim, IEnumerable<Run>> stamp) => new()
    {
        Columns = definition.Columns.Count == 3 ? definition.Columns : ["Action", "Owner", "Due"],
        Widths = definition.ColumnWidths,
        Rows = claims.Select(c => new TableRow
        {
            Cells =
            [
                TableCell.Of([Run.Plain(Sentence(c.Text)), .. stamp(c)]),
                c.Owner is { } owner ? TableCell.Of(owner) : TableCell.Of(Run.Note(NoOwner)),
                c.Due is { } due ? TableCell.Of(due) : TableCell.Of(Run.Note(NoDate)),
            ],
        }).ToList(),
    };

    private static ListBlock Agenda(IReadOnlyList<AgendaItem> agenda, IReadOnlyList<Claim> claims, TranscriptIndex transcript, bool link, bool coverageChecked)
    {
        var items = new List<ListItem>();
        for (var i = 0; i < agenda.Count; i++)
        {
            var number = i + 1;
            var evidence = claims.Where(c => c.AgendaItem == number).OrderBy(c => c.Line ?? int.MaxValue).FirstOrDefault();
            List<Run> runs = [Run.Plain(agenda[i].Text.Trim())];
            if (evidence is not null && transcript.Find(evidence.Line) is { } entry)
            {
                runs.Add(Run.Note(" (discussed"));
                if (link)
                {
                    runs.Add(Run.Note(" from "));
                    runs.Add(Run.Timestamp(entry.Start));
                }

                runs.Add(Run.Note(")"));
            }
            else if (agenda[i].Covered)
            {
                runs.Add(Run.Note(" (marked as covered)"));
            }
            else if (!coverageChecked)
            {
                runs.Add(Run.Note(" (coverage not checked: the agenda was not part of the inputs)"));
            }
            else
            {
                runs.Add(Run.Note(" — " + NotReached));
            }

            items.Add(new ListItem { Runs = runs });
        }

        return new ListBlock { Style = ListStyle.Numbered, Items = items };
    }

    private static List<Block> FollowUpEmail(IReadOnlyList<Claim> claims, GenerationFacts facts, Func<Claim, IEnumerable<Run>> stamp)
    {
        var decisions = claims.Where(c => c.Kind == ClaimKinds.Decision).ToList();
        var actions = claims.Where(c => c.Kind == ClaimKinds.Action).ToList();
        var blocks = new List<Block>
        {
            new ParagraphBlock { Runs = [Run.Plain("Hello everyone,")] },
            new ParagraphBlock { Runs = [Run.Plain(facts.Title is { Length: > 0 } title ? $"Thank you for joining {title}. Here is what we agreed." : "Thank you for joining. Here is what we agreed.")] },
        };
        if (decisions.Count == 0 && actions.Count == 0)
        {
            blocks.Add(Note("No decisions or commitments were recorded."));
        }

        if (decisions.Count > 0)
        {
            blocks.Add(new ParagraphBlock { Runs = [Run.Bold("Decisions")] });
            blocks.Add(new ListBlock { Items = decisions.Select(c => new ListItem { Runs = [Run.Plain(Sentence(c.Text)), .. stamp(c)] }).ToList() });
        }

        if (actions.Count > 0)
        {
            blocks.Add(new ParagraphBlock { Runs = [Run.Bold("Actions")] });
            blocks.Add(new ListBlock
            {
                Items = actions.Select(c => new ListItem
                {
                    Runs = [Run.Plain(Sentence(c.Text).TrimEnd('.')), Run.Plain(" — "), c.Owner is { } o ? Run.Plain(o) : Run.Note(NoOwner), Run.Plain(", "), c.Due is { } d ? Run.Plain(d) : Run.Note(NoDate), .. stamp(c)],
                }).ToList(),
            });
        }

        blocks.Add(new ParagraphBlock { Runs = [Run.Plain("Best regards")] });
        return blocks;
    }
}
