using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Model.Modules;

namespace Memento.Documents.Render;

/// <summary>The fixed, fictional sample minutes the Style editor previews (DESIGN.md §13): title, meta, Executive summary, Decisions, Action items.</summary>
public static class SampleDocument
{
    public static Document Minutes { get; } = new()
    {
        Id = "style-sample",
        Title = "Design review: library screen",
        Meta = new DocumentMeta
        {
            Kind = "Meeting minutes",
            RecordedAt = new DateTimeOffset(2026, 10, 5, 16, 0, 0, TimeSpan.FromHours(1)),
            DurationMs = 70 * 60 * 1000,
            ParticipantCount = 4,
        },
        Rows =
        [
            DocumentRow.Of(new ModuleBlock
            {
                Id = "sample-summary",
                Type = ModuleIds.ExecutiveSummary,
                Title = "Executive summary",
                Provenance = Provenance.FromAi(),
                Blocks =
                [
                    new ParagraphBlock
                    {
                        Runs =
                        [
                            Run.Plain("The team approved the library layout for build with two changes: rows stay at 68 px with status pills only for stages that have run, and the processing card hides whenever a filter is active. The dark theme ships with the first version."),
                        ],
                    },
                ],
            }),
            DocumentRow.Of(new ModuleBlock
            {
                Id = "sample-decisions",
                Type = ModuleIds.Decisions,
                Title = "Decisions",
                Provenance = Provenance.FromAi(),
                Blocks =
                [
                    new ListBlock
                    {
                        Items =
                        [
                            new ListItem { Runs = [Run.Plain("Row height stays at 68 px.")] },
                            new ListItem { Runs = [Run.Plain("Status pills only for stages that have run; audio-only shows a caption.")] },
                            new ListItem { Runs = [Run.Plain("Dark theme ships in v1.")] },
                        ],
                    },
                ],
            }),
            DocumentRow.Of(new ModuleBlock
            {
                Id = "sample-actions",
                Type = ModuleIds.ActionItems,
                Title = "Action items",
                Provenance = Provenance.FromAi(),
                Blocks =
                [
                    new TableBlock
                    {
                        Columns = ["Action", "Owner", "Due"],
                        Widths = [2, 1, 1],
                        Rows =
                        [
                            new TableRow { Cells = [TableCell.Of("Update the library mockup"), TableCell.Of("Priya"), TableCell.Of("Wed")] },
                            new TableRow { Cells = [TableCell.Of("Produce the dark variant"), TableCell.Of("Sam"), TableCell.Of("Wed")] },
                            new TableRow { Cells = [TableCell.Of("Write empty-state copy"), TableCell.Of("Sam"), TableCell.Of("Fri")] },
                        ],
                    },
                ],
            }),
        ],
    };
}
