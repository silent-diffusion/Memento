using System.Globalization;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Model.Modules;

namespace Memento.Documents.Tests.DocumentModel.Support;

/// <summary>Synthetic, fictional documents for the renderer and exporter fixtures.</summary>
internal static class SampleDocuments
{
    private static readonly DateTimeOffset Recorded = new(2026, 10, 5, 16, 0, 0, TimeSpan.FromHours(1));

    private static readonly string[] Speakers = ["Sam Okafor", "Priya Natarajan", "Lee Chen", "Me"];

    private static readonly string[] Lines =
    [
        "Let's start with the mockups at 1440 pixels and see whether the rows feel tight.",
        "They read well at sixty-eight, but a pill on every row makes the list busy.",
        "What if we only show pills for stages that have run, and a quiet caption for audio-only?",
        "That works for me. The processing card should hide while a filter is active.",
        "Agreed. I'll update the library mockup by Wednesday.",
        "On the dark theme, people switch themes with the system, so a light-only release would look unfinished.",
        "Nobody disagrees, then: the dark theme ships in the first version.",
        "I can produce the dark variant by Wednesday as well.",
        "Should the grid view show thumbnails for video recordings?",
        "Let's leave that open until video capture is planned.",
        "The empty state still needs copy; I'll write it by Friday.",
        "Good. Next time we walk through the recording screen.",
    ];

    /// <summary>The Document viewer render's minutes (DocView.dc.html), plus a Full transcript set small, so print and Word paginate.</summary>
    public static Document MeetingMinutes() => new()
    {
        Id = "doc-20261005-minutes",
        Title = "Design review: library screen",
        StyleId = "corporate",
        Meta = new DocumentMeta
        {
            Kind = "Meeting minutes",
            RecordedAt = Recorded,
            DurationMs = 70 * 60 * 1000,
            Platform = "Zoom",
            RecordingTitle = "Design review: library screen",
            RecordingId = "20261005-160000-k3f9ab",
        },
        Generation = new GenerationReference
        {
            RecordId = "gen-0001",
            TemplateId = "meeting-minutes",
            StyleId = "corporate",
            ProviderId = "anthropic",
            GeneratedAt = Recorded.AddHours(25).AddMinutes(14),
            DurationMs = 38_000,
        },
        Version = 2,
        CreatedAt = Recorded.AddHours(25).AddMinutes(14),
        ModifiedAt = Recorded.AddHours(25).AddMinutes(20),
        Rows =
        [
            DocumentRow.Of(Module("m01", ModuleIds.ExecutiveSummary, "Executive summary", TextSize.Larger, true, Provenance.FromAi("c1", "c2"),
                new ParagraphBlock
                {
                    Runs =
                    [
                        Run.Plain("The team approved the library layout for build with two changes: rows stay at 68 px with status pills shown only for stages that have run, and the processing card hides whenever a filter or search is active."),
                        Run.Timestamp(1122),
                        Run.Plain(" The dark theme will ship with the first version rather than following later."),
                        Run.Timestamp(2470),
                        Run.Plain(" The empty state needs copy; Sam owns it."),
                    ],
                })),
            DocumentRow.Of(
                Module("m02", ModuleIds.MeetingPurpose, "Meeting purpose", TextSize.Normal, false, Provenance.FromAi("c3"),
                    new ParagraphBlock { Runs = [Run.Plain("Agree the library layout before build starts.")] }),
                Module("m03", ModuleIds.Participants, "Participants", TextSize.Normal, false, Provenance.FromData(),
                    new ChipsBlock { Items = ["Sam Okafor", "Priya Natarajan", "Lee Chen", "Me"] })),
            DocumentRow.Of(Module("m04", ModuleIds.Agenda, "Agenda", TextSize.Normal, true, Provenance.FromAi("c4"),
                new ListBlock
                {
                    Items =
                    [
                        Item("Review mockups"),
                        Item("Row density and status pills"),
                        Item("Dark theme scope"),
                        new ListItem { Runs = [Run.Plain("Owners and next steps "), Run.Note("(reached with 8 minutes left)")] },
                    ],
                })),
            DocumentRow.Of(Module("m05", ModuleIds.Discussion, "Discussion summary", TextSize.Normal, true, Provenance.FromAi("c5", "c6", "c7"),
                new ParagraphBlock
                {
                    Runs =
                    [
                        Run.Plain("Sam opened with the 1440 px mockups and asked whether the row height felt tight."),
                        Run.Timestamp(1090),
                        Run.Plain(" Priya agreed that a pill on every row made the list busy. The proposal to show pills only for completed or running stages, and a quiet caption for audio-only recordings, was accepted without objection."),
                        Run.Timestamp(1145),
                        Run.Plain(" On the dark theme, Lee argued that Windows users switch themes with the system and a light-only first release would look unfinished; nobody disagreed."),
                        Run.Timestamp(2470),
                    ],
                })),
            DocumentRow.Of(
                Module("m06", ModuleIds.Decisions, "Decisions", TextSize.Normal, true, Provenance.FromAi("c8", "c9", "c10", "c11"),
                    new ListBlock
                    {
                        Items =
                        [
                            new ListItem { Runs = [Run.Plain("Row height stays at 68 px."), Run.Timestamp(1122)] },
                            new ListItem { Runs = [Run.Plain("Status pills only for stages that have run; audio-only shows a caption."), Run.Timestamp(1122)] },
                            new ListItem { Runs = [Run.Plain("Processing card hides while a filter or search is active."), Run.Timestamp(1171)] },
                            new ListItem { Runs = [Run.Plain("Dark theme ships in v1."), Run.Timestamp(2470)] },
                        ],
                    }),
                Module("m07", ModuleIds.ActionItems, "Action items", TextSize.Normal, true, Provenance.FromAi("c12", "c13", "c14"),
                    new TableBlock
                    {
                        Columns = ["Action", "Owner", "Due"],
                        Widths = [2, 1, 1],
                        Rows =
                        [
                            new TableRow { Cells = [TableCell.Of(Run.Plain("Update the library mockup"), Run.Timestamp(1160)), TableCell.Of("Priya"), TableCell.Of("Wed")] },
                            new TableRow { Cells = [TableCell.Of("Produce the dark variant"), TableCell.Of("Sam"), TableCell.Of("Wed")] },
                            new TableRow { Cells = [TableCell.Of(Run.Plain("Write empty-state copy"), Run.Timestamp(3725)), TableCell.Of("Sam"), TableCell.Of("Fri")] },
                        ],
                    })),
            DocumentRow.Of(
                Module("m08", ModuleIds.OpenQuestions, "Open questions", TextSize.Normal, false, Provenance.FromAi("c15", "c16"),
                    new ListBlock
                    {
                        Items =
                        [
                            Item("Should the grid view show thumbnails for video recordings?"),
                            Item("Where does \"Documents\" live if there is no sidebar?"),
                        ],
                    }),
                Module("m09", ModuleIds.NextMeeting, "Next meeting", TextSize.Normal, false, Provenance.FromAi("c17"),
                    new LabelValueBlock
                    {
                        Pairs =
                        [
                            new LabelValuePair { Label = "When", Runs = [Run.Plain("Thursday 9 October, 4:00 PM")] },
                            new LabelValuePair { Label = "Agenda", Runs = [Run.Plain("Recording screen walkthrough")] },
                        ],
                    })),
            DocumentRow.Of(Module("m10", ModuleIds.FullTranscript, "Full transcript", TextSize.Smaller, false, Provenance.FromData(), Transcript(72))),
        ],
    };

    /// <summary>Every block shape and run kind, in one-, two- and three-column rows with every text size.</summary>
    public static Document AllShapes() => new()
    {
        Id = "doc-all-shapes",
        Title = "Every module shape",
        StyleId = "corporate",
        Meta = new DocumentMeta { Kind = "Fixture", RecordedAt = Recorded, DurationMs = 42 * 60 * 1000, ParticipantCount = 3 },
        Version = 1,
        CreatedAt = Recorded,
        ModifiedAt = Recorded,
        Rows =
        [
            DocumentRow.Of(Module("s01", ModuleIds.CustomText, "Text and runs", TextSize.Larger, false, Provenance.FromUser(),
                new HeadingBlock { Level = 1, Runs = [Run.Plain("A sub-heading")] },
                new ParagraphBlock
                {
                    Runs =
                    [
                        Run.Plain("Plain text, "),
                        Run.Bold("bold"),
                        Run.Plain(", "),
                        Run.Italic("italic"),
                        Run.Plain(", "),
                        Run.BoldItalic("both"),
                        Run.Plain(" and a "),
                        Run.Note("quiet note"),
                        Run.Plain(", then a moment."),
                        Run.Timestamp(65.5, "1:05"),
                        Run.Plain(" Characters that need escaping: <tag> & \"quotes\" * _ [x] # 50% | pipe.\nA second line."),
                    ],
                },
                new HeadingBlock { Level = 2, Runs = [Run.Plain("A smaller sub-heading")] },
                new ParagraphBlock { Runs = [Run.Plain("After an hour"), Run.Timestamp(3725.25)] })),
            DocumentRow.Of(
                Module("s02", ModuleIds.Decisions, "Bulleted, nested", TextSize.Normal, true, Provenance.FromAi("a1"),
                    new ListBlock
                    {
                        Items =
                        [
                            new ListItem { Runs = [Run.Plain("First point")], Items = [Item("Nested point"), new ListItem { Runs = [Run.Plain("Nested with "), Run.Bold("emphasis")], Items = [Item("Third level")] }] },
                            new ListItem { Runs = [Run.Plain("Second point"), Run.Timestamp(130)] },
                        ],
                    }),
                Module("s03", ModuleIds.Agenda, "Numbered, nested", TextSize.Normal, true, Provenance.FromAi("a2"),
                    new ListBlock
                    {
                        Style = ListStyle.Numbered,
                        Items = [new ListItem { Runs = [Run.Plain("Opening")], Items = [Item("Welcome"), Item("Minutes of the last meeting")] }, Item("Budget"), Item("Close")],
                    })),
            DocumentRow.Of(
                Module("s04", ModuleIds.ActionItems, "Table", TextSize.Smaller, true, Provenance.FromAi("a3"),
                    new TableBlock
                    {
                        Columns = ["Action", "Owner", "Due"],
                        Widths = [2, 1, 1],
                        Rows =
                        [
                            new TableRow { Cells = [TableCell.Of(Run.Plain("Send the draft"), Run.Timestamp(300)), TableCell.Of("Jordan"), TableCell.Of("Mon")] },
                            new TableRow { Cells = [TableCell.Of("Book the room"), TableCell.Of(Run.Note("No owner was named")), TableCell.Of(Run.Note("No date was given"))] },
                        ],
                    }),
                Module("s05", ModuleIds.Participants, "Chips", TextSize.Normal, false, Provenance.FromData(),
                    new ChipsBlock { Items = ["Jordan Vale", "Alex Moreau", "Kim Ito"] }),
                Module("s06", ModuleIds.NextMeeting, "Label and value", TextSize.Larger, false, Provenance.FromAi("a4"),
                    new LabelValueBlock
                    {
                        Pairs =
                        [
                            new LabelValuePair { Label = "When", Runs = [Run.Plain("Not discussed")] },
                            new LabelValuePair { Label = "Agenda", Runs = [Run.Italic("To be agreed")] },
                        ],
                    })),
            DocumentRow.Of(
                Module("s07", ModuleIds.Quote, "Quote", TextSize.Normal, true, Provenance.FromAi("a5"),
                    new QuoteBlock { Runs = [Run.Plain("We should ship the smaller thing first.")], Attribution = "Alex Moreau", T = 512 },
                    new QuoteBlock { Runs = [Run.Plain("An unattributed remark.")] }),
                Module("s08", ModuleIds.Timeline, "Timeline", TextSize.Normal, true, Provenance.FromAi("a6"),
                    new TimelineBlock
                    {
                        Entries =
                        [
                            new TimelineEntry { T = 0, Runs = [Run.Plain("Opening")] },
                            new TimelineEntry { T = 512, Runs = [Run.Plain("Scope agreed")] },
                            new TimelineEntry { T = 3725, Runs = [Run.Bold("Close")] },
                        ],
                    })),
            DocumentRow.Of(Module("s09", ModuleIds.FullTranscript, "Transcript", TextSize.Smaller, false, Provenance.FromData(),
                new TranscriptBlock
                {
                    Chapters = [new TranscriptChapter { T = 0, Title = "Opening" }, new TranscriptChapter { T = 60, Title = "Scope" }],
                    Segments =
                    [
                        new TranscriptLine { Id = "s0001", Speaker = "Jordan Vale", T = 1.5, Text = "Shall we start?" },
                        new TranscriptLine { Id = "s0002", Speaker = "Alex Moreau", T = 12.25, Text = "Yes. Two things today." },
                        new TranscriptLine { Id = "s0003", Speaker = "Kim Ito", T = 64, Text = "First, the scope.\nThen the dates." },
                    ],
                })),
        ],
    };

    public static ModuleBlock Module(string id, string type, string title, TextSize size, bool link, Provenance provenance, params Block[] blocks) =>
        new() { Id = id, Type = type, Title = title, TextSize = size, LinkToTranscript = link, Provenance = provenance, Blocks = blocks };

    public static ListItem Item(string text) => new() { Runs = [Run.Plain(text)] };

    public static TranscriptBlock Transcript(int count) => new()
    {
        Chapters =
        [
            new TranscriptChapter { T = 0, Title = "Review mockups" },
            new TranscriptChapter { T = 1080, Title = "Row density and status pills" },
            new TranscriptChapter { T = 2460, Title = "Dark theme scope" },
            new TranscriptChapter { T = 3600, Title = "Owners and next steps" },
        ],
        Segments = Enumerable.Range(0, count).Select(i => new TranscriptLine
        {
            Id = "s" + (i + 1).ToString("0000", CultureInfo.InvariantCulture),
            Speaker = Speakers[i % Speakers.Length],
            T = 15 + (i * 58.5),
            Text = Lines[i % Lines.Length],
        }).ToList(),
    };
}
