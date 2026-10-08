using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Memento.Documents.Agenda.Text;

/// <summary>
/// Turns lines into an ordered, levelled list of agenda items: finds the title, meeting details, headings, list
/// markers and wrapped lines, assigns levels from indentation and marker kind, and checks the numbering. Every
/// format parser feeds its lines through here, so all formats share the same rules and the same uncertainty reasons.
/// </summary>
internal static partial class AgendaStructurer
{
    private const int ImplicitHeadingLevel = 100;

    /// <summary>Ten times the length at which an item is marked too long.</summary>
    private const int MaxJoinedLength = AgendaLimits.LongItemLength * 10;

    public static StructuredAgenda Structure(IReadOnlyList<SourceLine> lines, CancellationToken cancellationToken)
    {
        var entries = Classify(lines, cancellationToken);
        var details = new List<Entry>();
        var listDocument = IsListDocument(entries);

        var title = FindTitle(entries, listDocument, details);
        MarkDetails(entries, details);
        JoinContinuations(entries, listDocument);
        if (listDocument)
        {
            MarkSections(entries);
            MarkTrailer(entries, details);
        }

        var items = Place(entries, listDocument, cancellationToken);

        var warnings = new List<AgendaParseWarning>();
        if (details.Count > 0)
        {
            var content = details.OrderBy(d => d.Index).Select(d => d.Original).ToList();
            warnings.Add(new AgendaParseWarning(
                AgendaWarningCodes.DetailsSkipped,
                content.Count == 1
                    ? "One line that looks like meeting details, not an agenda item, was left out of the list. Add it here if it belongs in the agenda."
                    : string.Create(CultureInfo.InvariantCulture, $"{content.Count} lines that look like meeting details, not agenda items, were left out of the list. Add any that belong in the agenda here."),
                string.Join('\n', content),
                details.OrderBy(d => d.Index).First().Line.Location));
        }

        return new StructuredAgenda(items, title, warnings);
    }

    private static List<Entry> Classify(IReadOnlyList<SourceLine> lines, CancellationToken cancellationToken)
    {
        var entries = new List<Entry>(lines.Count);
        var previousBlank = true;
        for (var i = 0; i < lines.Count; i++)
        {
            if ((i & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var line = lines[i];
            var text = Clean(line.Text);
            var entry = new Entry(line, entries.Count, text) { PrecededByBlank = previousBlank };
            previousBlank = text.Length == 0;
            entries.Add(entry);
            if (text.Length == 0)
            {
                entry.Kind = EntryKind.Blank;
                continue;
            }

            entry.Reasons.AddRange(line.Reasons);
            if (line.IsTitle || line.HeadingLevel is not null)
            {
                entry.Kind = EntryKind.Heading;
                entry.HeadingLevel = line.IsTitle ? 0 : line.HeadingLevel!.Value;
                entry.IsTitleStyle = line.IsTitle;
                entry.Text = StripHeadingMarker(text);
                continue;
            }

            var marker = line.Marker;
            var rest = text;
            if (marker is null)
            {
                if (MarkerParser.TryParseMarker(text, out var parsed, out var after))
                {
                    marker = parsed;
                    rest = after;
                }
            }
            else if (MarkerParser.TryParseMarker(text, out var typed, out var afterTyped) && typed.Style != MarkerStyle.Bullet)
            {
                // Automatic numbering plus a typed number ("1. 1. Welcome"): keep the automatic one, drop the typed one.
                rest = afterTyped;
            }

            var time = line.Time;
            if (MarkerParser.TryParseTimePrefix(rest, out var leading, out var afterTime))
            {
                time = time is null ? leading : time;
                rest = afterTime;
            }
            else if (MarkerParser.TryParseTrailingTime(rest, out var trailing, out var beforeTime))
            {
                time ??= trailing;
                rest = beforeTime;
            }

            entry.Marker = marker;
            entry.Time = time;
            entry.Text = rest.Trim();
            if (entry.Text.Length == 0)
            {
                // A marker or time with nothing after it: keep what was written rather than lose it.
                entry.Text = text;
                entry.Marker = null;
                entry.Time = null;
            }

            if (marker is null && time is null && IsHeadingLike(entry.Text))
            {
                entry.Kind = EntryKind.Heading;
                entry.HeadingLevel = ImplicitHeadingLevel;
                entry.Text = entry.Text.TrimEnd(':', '：').TrimEnd();
            }
            else
            {
                entry.Kind = EntryKind.Item;
            }
        }

        return entries;
    }

    private static bool IsListDocument(List<Entry> entries)
    {
        var items = entries.Where(e => e.Kind == EntryKind.Item).ToList();
        var marked = items.Count(e => e.Marker is not null || e.Time is not null);
        return marked >= 2 && marked * 2 >= items.Count;
    }

    private static string? FindTitle(List<Entry> entries, bool listDocument, List<Entry> details)
    {
        var content = entries.Where(e => e.Kind != EntryKind.Blank).ToList();
        if (content.Count == 0)
        {
            return null;
        }

        string? title = null;
        var first = content[0];
        if (first.IsTitleStyle)
        {
            title = first.Text;
            first.Kind = EntryKind.Title;
        }

        // A heading that names the agenda ("Agenda", "Agenda:", "Project kickoff agenda") before any list item:
        // everything before it is preamble (greetings, details), and it is the title.
        for (var i = 0; i < content.Count; i++)
        {
            var entry = content[i];
            if (entry.Kind == EntryKind.Item && (entry.Marker is not null || entry.Time is not null))
            {
                break;
            }

            var candidate = entry.Kind == EntryKind.Heading || (entry.Kind == EntryKind.Item && (i == 0 || entry.Text.EndsWith(':')));
            if (!candidate || entry.Text.Length > 80 || !RegexGuard.IsMatch(AgendaWordPattern(), entry.Text))
            {
                continue;
            }

            var bare = RegexGuard.IsMatch(BareAgendaPattern(), entry.Text);
            var named = title is null && bare && i > 0 && content[0].Kind is EntryKind.Item or EntryKind.Heading &&
                content[0].Marker is null && content[0].Time is null && !RegexGuard.IsMatch(DetailsLinePattern(), content[0].Original);
            if (named)
            {
                // "Weekly design sync" then "Agenda:": the first line names the meeting.
                title = content[0].Text.TrimEnd(':', '：').TrimEnd();
                content[0].Kind = EntryKind.Title;
            }

            for (var j = 0; j < i; j++)
            {
                if (content[j].Kind != EntryKind.Title)
                {
                    content[j].Kind = EntryKind.Details;
                    details.Add(content[j]);
                }
            }

            if (title is null)
            {
                title = entry.Text.TrimEnd(':', '：').TrimEnd();
                entry.Kind = EntryKind.Title;
            }
            else if (bare)
            {
                // A bare "Agenda" heading under the title is structure, not content.
                entry.Kind = EntryKind.Title;
            }
            else
            {
                entry.Kind = EntryKind.Details;
                details.Add(entry);
            }

            return title;
        }

        if (title is not null)
        {
            return title;
        }

        // A single top heading over the rest ("# Team sync" then "## Updates", or a Word Heading 1 over Heading 2s).
        var headings = content.Where(e => e.Kind == EntryKind.Heading && e.HeadingLevel < ImplicitHeadingLevel).ToList();
        if (first.Kind == EntryKind.Heading && first.HeadingLevel < ImplicitHeadingLevel && content.Count > 1)
        {
            var top = headings.Min(h => h.HeadingLevel);
            if (first.HeadingLevel == top && headings.Count(h => h.HeadingLevel == top) == 1)
            {
                first.Kind = EntryKind.Title;
                return first.Text;
            }
        }

        // A plain first line over a marked list, with no other plain line heading a list: the meeting's name.
        if (listDocument && first.Kind is EntryKind.Item or EntryKind.Heading && first.Marker is null && first.Time is null && content.Count > 1)
        {
            var next = content[1];
            var otherSections = content.Skip(1).Where((e, k) =>
                e.Kind is EntryKind.Item or EntryKind.Heading && e.Marker is null && e.Time is null &&
                k + 2 < content.Count && (content[k + 2].Marker is not null || content[k + 2].Time is not null)).Any();
            if ((next.Marker is not null || next.Time is not null) && !otherSections &&
                (first.Kind == EntryKind.Item || first.HeadingLevel == ImplicitHeadingLevel))
            {
                first.Kind = EntryKind.Title;
                return first.Text.TrimEnd(':', '：').TrimEnd();
            }
        }

        return null;
    }

    private static void MarkDetails(List<Entry> entries, List<Entry> details)
    {
        // Page furniture anywhere ("Page 2 of 3"); meeting details before the first agenda item: "Date: …",
        // "Location: …", a bare date, an "Attendees" block.
        foreach (var entry in entries)
        {
            if (entry.Kind is EntryKind.Blank or EntryKind.Title or EntryKind.Details)
            {
                continue;
            }

            if (RegexGuard.IsMatch(PageFurniturePattern(), entry.Original))
            {
                entry.Kind = EntryKind.Details;
                details.Add(entry);
            }
        }

        var inAttendees = false;
        foreach (var entry in entries)
        {
            if (entry.Kind is EntryKind.Blank)
            {
                inAttendees = false;
                continue;
            }

            if (entry.Kind is EntryKind.Title or EntryKind.Details)
            {
                continue;
            }

            if (entry.Kind == EntryKind.Heading && RegexGuard.IsMatch(PeopleHeadingPattern(), entry.Text))
            {
                entry.Kind = EntryKind.Details;
                details.Add(entry);
                inAttendees = true;
                continue;
            }

            if (inAttendees && entry.Kind == EntryKind.Item)
            {
                entry.Kind = EntryKind.Details;
                details.Add(entry);
                continue;
            }

            if (entry.Kind == EntryKind.Item && entry.Marker is null && (RegexGuard.IsMatch(DetailsLinePattern(), entry.Original) || RegexGuard.IsMatch(DateLinePattern(), entry.Original)))
            {
                entry.Kind = EntryKind.Details;
                details.Add(entry);
                continue;
            }

            if (entry.Kind == EntryKind.Item && entry.Marker is null && entry.Time is not null && RegexGuard.IsMatch(TimeOnlyPattern(), entry.Original))
            {
                entry.Kind = EntryKind.Details;
                details.Add(entry);
                continue;
            }

            break;
        }
    }

    private static void JoinContinuations(List<Entry> entries, bool listDocument)
    {
        if (!listDocument)
        {
            return;
        }

        Entry? previous = null;
        foreach (var entry in entries)
        {
            if (entry.Kind != EntryKind.Item)
            {
                previous = entry.Kind == EntryKind.Continuation ? previous : null;
                continue;
            }

            // An item that has grown past MaxJoinedLength takes no more wrapped lines: each join copies the text, so an
            // unbounded run of continuation lines would be quadratic. The rest become items of their own.
            if (previous is not null && previous.Text.Length < MaxJoinedLength && entry.Marker is null && entry.Time is null && entry.Line.MayContinue && !entry.PrecededByBlank &&
                (previous.Marker is not null || previous.Time is not null) &&
                (entry.Indent > previous.Indent || (StartsLowercase(entry.Text) && !EndsSentence(previous.Text) && entry.Indent >= previous.Indent)))
            {
                previous.Text = JoinWrapped(previous.Text, entry.Text);
                previous.Reasons.AddRange(entry.Reasons);
                previous.HeadingMergeSuspected |= entry.Line.HeadingMergeSuspected;
                entry.Kind = EntryKind.Continuation;
                continue;
            }

            previous = entry;
        }
    }

    private static void MarkSections(List<Entry> entries)
    {
        // A plain line that heads a marked list is a section heading ("Morning", then bullets), unless it sits inside
        // a numbered run that carries on after it ("2. Budget", "Lunch", "3. Roadmap").
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            if (entry.Kind != EntryKind.Item || entry.Marker is not null || entry.Time is not null)
            {
                continue;
            }

            var next = NextContent(entries, i);
            if (next is null || next.Kind != EntryKind.Item || next.Indent < entry.Indent)
            {
                continue;
            }

            if (next.Marker is null && !(next.Time is not null && entry.PrecededByBlank))
            {
                continue;
            }

            var previous = PreviousContent(entries, i);
            if (previous?.Marker is { Value: { } before } previousMarker && next.Marker is { Value: { } after } nextMarker &&
                Family(previousMarker) == Family(nextMarker) && after == before + 1)
            {
                continue;
            }

            entry.Kind = EntryKind.Heading;
            entry.HeadingLevel = ImplicitHeadingLevel;
        }
    }

    private static void MarkTrailer(List<Entry> entries, List<Entry> details)
    {
        // Plain lines after the last list item and a blank line ("Thanks,", a name): a sign-off, not items.
        var lastMarked = entries.FindLastIndex(e => e.Kind == EntryKind.Item && (e.Marker is not null || e.Time is not null));
        if (lastMarked < 0)
        {
            return;
        }

        var tail = entries.Skip(lastMarked + 1).Where(e => e.Kind != EntryKind.Blank && e.Kind != EntryKind.Continuation).ToList();
        if (tail.Count is 0 or > 4 || tail.Any(e => e.Kind != EntryKind.Item || e.Marker is not null || e.Time is not null) || !tail[0].PrecededByBlank)
        {
            return;
        }

        if (!RegexGuard.IsMatch(SignOffPattern(), tail[0].Text))
        {
            return;
        }

        foreach (var entry in tail)
        {
            entry.Kind = EntryKind.Details;
            details.Add(entry);
        }
    }

    private static List<ParsedAgendaItem> Place(List<Entry> entries, bool listDocument, CancellationToken cancellationToken)
    {
        var hasOutline = entries.Any(e => e.Marker?.Style == MarkerStyle.Outline);
        var stack = new List<Frame>();
        var lastByKey = new Dictionary<(string Family, int Indent), (int Value, string Label)>();
        var lastByPrefix = new Dictionary<string, (int Value, string Label)>(StringComparer.Ordinal);
        var placed = new List<(Entry Entry, int Level)>();

        foreach (var entry in entries)
        {
            if (entry.Kind == EntryKind.Heading)
            {
                while (stack.Count > 0 && (!stack[^1].IsHeading || stack[^1].HeadingLevel >= entry.HeadingLevel))
                {
                    stack.RemoveAt(stack.Count - 1);
                }

                var level = stack.Count == 0 ? 0 : stack[^1].Level + 1;
                stack.Add(new Frame { IsHeading = true, HeadingLevel = entry.HeadingLevel, Level = level, Indent = entry.Indent });
                placed.Add((entry, level));
                continue;
            }

            if (entry.Kind != EntryKind.Item)
            {
                continue;
            }

            if ((placed.Count & 63) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var marker = entry.Marker;
            if (marker is { Style: MarkerStyle.Decimal } && hasOutline)
            {
                marker = marker with { Style = MarkerStyle.Outline };
                entry.Marker = marker;
            }

            if (marker is { Style: MarkerStyle.Outline })
            {
                placed.Add((entry, PlaceOutline(entry, marker, stack, lastByPrefix)));
                continue;
            }

            if (marker is null && listDocument)
            {
                placed.Add((entry, PlacePlainSibling(entry, stack)));
                continue;
            }

            placed.Add((entry, PlaceListItem(entry, stack, lastByKey)));
        }

        var items = new List<ParsedAgendaItem>(placed.Count);
        var previousLevel = -1;
        foreach (var (entry, rawLevel) in placed)
        {
            var level = Math.Clamp(rawLevel, 0, previousLevel + 1);
            previousLevel = level;
            var reasons = new List<string>();
            reasons.AddRange(ItemChecks.Check(entry.Text, entry.Marker, entry.HeadingMergeSuspected || entry.Line.HeadingMergeSuspected));
            reasons.AddRange(entry.Reasons);
            var distinct = reasons.Distinct(StringComparer.Ordinal).Take(2).ToList();
            items.Add(new ParsedAgendaItem(
                entry.Text,
                distinct.Count > 0,
                distinct.Count > 0 ? string.Join(' ', distinct) : null,
                level,
                entry.Line.Location,
                entry.Time,
                entry.Marker?.Label));
        }

        return items;
    }

    private static int PlaceOutline(Entry entry, ListMarker marker, List<Frame> stack, Dictionary<string, (int Value, string Label)> lastByPrefix)
    {
        while (stack.Count > 0 && !stack[^1].IsHeading)
        {
            stack.RemoveAt(stack.Count - 1);
        }

        var baseLevel = stack.Count == 0 ? 0 : stack[^1].Level + 1;
        var level = baseLevel + marker.Depth - 1;
        var prefix = marker.OutlinePrefix ?? string.Empty;
        var value = marker.Value!.Value;
        var label = marker.Label!;
        if (lastByPrefix.TryGetValue(prefix, out var last))
        {
            CheckSequence(entry, last.Value, last.Label, value, label, isNewList: false);
        }
        else if (value != 1)
        {
            entry.Reasons.Add(UncertainReasons.NumberingStartsLate(label));
        }

        lastByPrefix[prefix] = (value, label);

        // Deeper parts restart under a new parent: forget "1.3" once "2" begins, so "2.1" is expected.
        foreach (var key in lastByPrefix.Keys.Where(k => k.Length > 0 && (k == label || k.StartsWith(label + ".", StringComparison.Ordinal))).ToList())
        {
            lastByPrefix.Remove(key);
        }

        stack.Add(new Frame { Family = "outline", Rank = 2, Indent = entry.Indent, Level = level, Style = MarkerStyle.Outline });
        return level;
    }

    private static int PlacePlainSibling(Entry entry, List<Frame> stack)
    {
        // An unmarked or time-only line in a marked list ("Lunch" between "2." and "3.", "10:00 Welcome"): a sibling of
        // the list items at its indent, without ending their numbering, or a child when indented under them.
        while (stack.Count > 0 && !stack[^1].IsHeading && stack[^1].Indent > entry.Indent)
        {
            stack.RemoveAt(stack.Count - 1);
        }

        if (stack.Count > 0 && !stack[^1].IsHeading && stack[^1].Indent == entry.Indent)
        {
            return stack[^1].Level;
        }

        var level = stack.Count == 0 ? 0 : stack[^1].Level + 1;
        stack.Add(new Frame { Family = "plain", Rank = 8, Indent = entry.Indent, Level = level });
        return level;
    }

    private static int PlaceListItem(Entry entry, List<Frame> stack, Dictionary<(string Family, int Indent), (int Value, string Label)> lastByKey)
    {
        var marker = ResolveRoman(entry, stack);
        var family = Family(marker);
        var rank = Rank(marker);
        var indent = entry.Indent;

        while (stack.Count > 0 && !stack[^1].IsHeading && stack[^1].Indent > indent)
        {
            stack.RemoveAt(stack.Count - 1);
        }

        if (marker is null)
        {
            // A plain line is never nested by marker kind, only by indentation: at the indent of a marked list it
            // closes that list and sits beside the plain lines before it.
            while (stack.Count > 0 && !stack[^1].IsHeading && stack[^1].Indent == indent && stack[^1].Family != family)
            {
                stack.RemoveAt(stack.Count - 1);
            }
        }

        Frame? frame = null;
        if (stack.Count > 0 && !stack[^1].IsHeading && stack[^1].Indent == indent)
        {
            var top = stack[^1];
            if (top.Family == family)
            {
                frame = top;
            }
            else
            {
                var outer = stack.FindLastIndex(f => !f.IsHeading && f.Indent == indent && f.Family == family);
                if (outer >= 0)
                {
                    stack.RemoveRange(outer + 1, stack.Count - outer - 1);
                    frame = stack[outer];
                }
                else if (rank <= top.Rank)
                {
                    while (stack.Count > 0 && !stack[^1].IsHeading && stack[^1].Indent == indent && stack[^1].Rank > rank)
                    {
                        stack.RemoveAt(stack.Count - 1);
                    }

                    if (stack.Count > 0 && !stack[^1].IsHeading && stack[^1].Indent == indent)
                    {
                        // Same indent, different marker, not nested: the same level with a new marker style.
                        var sibling = stack[^1];
                        stack[^1] = new Frame { Family = family, Rank = rank, Indent = indent, Level = sibling.Level, Style = marker?.Style ?? MarkerStyle.None };
                        frame = stack[^1];
                        frame.IsNew = true;
                    }
                }
            }
        }

        if (frame is null)
        {
            var level = stack.Count == 0 ? 0 : stack[^1].Level + 1;
            frame = new Frame { Family = family, Rank = rank, Indent = indent, Level = level, Style = marker?.Style ?? MarkerStyle.None, IsNew = true };
            stack.Add(frame);
        }

        if (marker is { Value: { } value, Label: { } label })
        {
            if (!frame.IsNew && frame.LastValue is { } lastValue)
            {
                CheckSequence(entry, lastValue, frame.LastLabel!, value, label, isNewList: false);
            }
            else if (lastByKey.TryGetValue((family, indent), out var last))
            {
                CheckSequence(entry, last.Value, last.Label, value, label, isNewList: true);
            }
            else if (value != 1)
            {
                entry.Reasons.Add(UncertainReasons.NumberingStartsLate(label));
            }

            frame.LastValue = value;
            frame.LastLabel = label;
            lastByKey[(family, indent)] = (value, label);
        }

        frame.IsNew = false;
        return frame.Level;
    }

    private static void CheckSequence(Entry entry, int lastValue, string lastLabel, int value, string label, bool isNewList)
    {
        if (value == lastValue + 1 || value == 1)
        {
            return;
        }

        if (isNewList)
        {
            // A list after a heading may restart at 1 or carry on; anything else is a gap.
            entry.Reasons.Add(value > lastValue ? UncertainReasons.NumberingSkips(lastLabel, label) : UncertainReasons.NumberingGoesBack(lastLabel, label));
            return;
        }

        entry.Reasons.Add(
            value == lastValue ? UncertainReasons.NumberingRepeats(label)
            : value > lastValue ? UncertainReasons.NumberingSkips(lastLabel, label)
            : UncertainReasons.NumberingGoesBack(lastLabel, label));
    }

    private static ListMarker? ResolveRoman(Entry entry, List<Frame> stack)
    {
        var marker = entry.Marker;
        if (marker?.AlternateRomanValue is not { } roman)
        {
            return marker;
        }

        var upper = marker.Style == MarkerStyle.UpperLetter;
        var sameIndent = stack.LastOrDefault(f => !f.IsHeading && f.Indent == entry.Indent);
        var asRoman = marker with { Style = upper ? MarkerStyle.UpperRoman : MarkerStyle.LowerRoman, Value = roman, AlternateRomanValue = null };
        var asLetter = marker with { AlternateRomanValue = null };
        ListMarker chosen;
        if (sameIndent is { Style: MarkerStyle.LowerRoman or MarkerStyle.UpperRoman })
        {
            chosen = asRoman;
        }
        else if (sameIndent is { Style: MarkerStyle.LowerLetter or MarkerStyle.UpperLetter, LastValue: { } last } && last == marker.Value - 1)
        {
            chosen = asLetter;
        }
        else
        {
            chosen = roman == 1 ? asRoman : asLetter;
        }

        entry.Marker = chosen;
        return chosen;
    }

    private static string Family(ListMarker? marker) => marker?.Style switch
    {
        null or MarkerStyle.None => "plain",
        MarkerStyle.Bullet => "bullet" + marker.BulletFamily.ToString(CultureInfo.InvariantCulture),
        MarkerStyle.Decimal or MarkerStyle.Outline => "number",
        _ => marker.Style.ToString(),
    };

    private static int Rank(ListMarker? marker) => marker?.Style switch
    {
        MarkerStyle.UpperRoman => 0,
        MarkerStyle.UpperLetter => 1,
        MarkerStyle.Decimal or MarkerStyle.Outline => 2,
        MarkerStyle.LowerLetter => 3,
        MarkerStyle.LowerRoman => 4,
        MarkerStyle.Bullet => 5 + marker.BulletFamily,
        MarkerStyle.Checkbox => 5,
        _ => 8,
    };

    private static Entry? NextContent(List<Entry> entries, int index)
    {
        for (var i = index + 1; i < entries.Count; i++)
        {
            if (entries[i].Kind is EntryKind.Item or EntryKind.Heading)
            {
                return entries[i];
            }
        }

        return null;
    }

    private static Entry? PreviousContent(List<Entry> entries, int index)
    {
        for (var i = index - 1; i >= 0; i--)
        {
            if (entries[i].Kind is EntryKind.Item or EntryKind.Heading)
            {
                return entries[i];
            }
        }

        return null;
    }

    private static string Clean(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (c is '​' or '‌' or '‍' or '﻿' or '­')
            {
                continue;
            }

            if (char.IsWhiteSpace(c) || c == ' ')
            {
                space = builder.Length > 0;
                continue;
            }

            if (space)
            {
                builder.Append(' ');
                space = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    private static string StripHeadingMarker(string text) =>
        MarkerParser.TryParseMarker(text, out var marker, out var rest) && marker.Style != MarkerStyle.None && rest.Trim().Length > 0
            ? rest.Trim()
            : text;

    private static bool IsHeadingLike(string text)
    {
        if (text.Length > 80)
        {
            return false;
        }

        if ((text.EndsWith(':') || text.EndsWith('：')) && text.Count(c => c == ':') == 1 && text.Length >= 3)
        {
            return true;
        }

        var letters = text.Count(char.IsLetter);
        return letters >= 3 && text.Length <= 60 && !text.Any(char.IsLower) && text.Split(' ').Length <= 8;
    }

    private static bool StartsLowercase(string text) => text.Length > 0 && char.IsLower(text[0]);

    private static bool EndsSentence(string text) => text.Length > 0 && text[^1] is '.' or '!' or '?' or ':';

    private static string JoinWrapped(string first, string second)
    {
        if (first.Length > 1 && first[^1] == '-' && char.IsLetter(first[^2]) && StartsLowercase(second))
        {
            return first[..^1] + second;
        }

        return first + " " + second;
    }

    [GeneratedRegex(@"\bagenda\b", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex AgendaWordPattern();

    [GeneratedRegex(@"^(?:the\s+|meeting\s+|today's\s+)?agenda\s*[:：]?$", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex BareAgendaPattern();

    [GeneratedRegex(@"^(?:date|day|time|when|where|location|venue|place|room|address|chair|chaired by|chairperson|facilitator|facilitated by|organi[sz]er|host|hosted by|note[- ]?taker|minutes|scribe|dial[- ]in|meeting link|link|call|zoom|teams|meet|conference|meeting id|passcode|objective|purpose|subject|re|from|to|cc|sent|attendees|participants|present|invitees|apologies|absent|guests|duration)\s*[:：]\s*\S", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex DetailsLinePattern();

    [GeneratedRegex(@"^(?:attendees|participants|present|invitees|apologies|absent|attendance|distribution|guests|people|who)\s*[:：]?$", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex PeopleHeadingPattern();

    [GeneratedRegex(@"^(?:(?:mon|tues?|wed(?:nes)?|thu(?:rs)?|fri|sat(?:ur)?|sun)(?:day)?\.?,?\s+)?(?:[0-9]{1,2}(?:st|nd|rd|th)?\s+(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*\.?,?\s+[0-9]{4}|(?:jan|feb|mar|apr|may|jun|jul|aug|sep|sept|oct|nov|dec)[a-z]*\.?\s+[0-9]{1,2}(?:st|nd|rd|th)?,?\s+[0-9]{4}|[0-9]{4}-[0-9]{2}-[0-9]{2}|[0-9]{1,2}[/.][0-9]{1,2}[/.][0-9]{2,4})(?:\s*[,·|–—-]\s*.*)?$", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex DateLinePattern();

    [GeneratedRegex(@"^\s*[0-9]{1,2}[:.h][0-9]{2}(?:\s*[ap]\.?m\.?)?\s*(?:-|–|—|to)\s*[0-9]{1,2}[:.h][0-9]{2}(?:\s*[ap]\.?m\.?)?\s*$", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex TimeOnlyPattern();

    [GeneratedRegex(@"^(?:page\s+[0-9]+(?:\s+of\s+[0-9]+)?|[0-9]+\s*/\s*[0-9]+|-\s*[0-9]+\s*-)$", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex PageFurniturePattern();

    [GeneratedRegex(@"^(?:thanks|thank you|many thanks|best|best regards|regards|kind regards|cheers|see you|talk soon)\b", RegexOptions.IgnoreCase, RegexGuard.TimeoutMilliseconds)]
    private static partial Regex SignOffPattern();

    private enum EntryKind
    {
        Blank,
        Item,
        Heading,
        Title,
        Details,
        Continuation,
    }

    private sealed class Entry(SourceLine line, int index, string original)
    {
        public SourceLine Line { get; } = line;

        public int Index { get; } = index;

        /// <summary>The cleaned line as written, before markers and times were taken off.</summary>
        public string Original { get; } = original;

        public string Text { get; set; } = original;

        public EntryKind Kind { get; set; }

        public ListMarker? Marker { get; set; }

        public string? Time { get; set; }

        public int Indent => Line.Indent;

        public int HeadingLevel { get; set; }

        public bool IsTitleStyle { get; set; }

        public bool PrecededByBlank { get; init; }

        public bool HeadingMergeSuspected { get; set; }

        public List<string> Reasons { get; } = [];
    }

    private sealed class Frame
    {
        public bool IsHeading { get; init; }

        public int HeadingLevel { get; init; }

        public int Level { get; init; }

        public int Indent { get; init; }

        public string Family { get; init; } = "plain";

        public int Rank { get; init; }

        public MarkerStyle Style { get; init; }

        public int? LastValue { get; set; }

        public string? LastLabel { get; set; }

        public bool IsNew { get; set; }
    }
}
