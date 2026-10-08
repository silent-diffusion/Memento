using System.Diagnostics;
using System.Globalization;
using Memento.AI;
using Memento.AI.Payload;
using Memento.Documents.Model;
using Memento.Documents.Model.Modules;
using Memento.Documents.Model.Records;
using Memento.Documents.Templates;
using Memento.Generation.Documents;

namespace Memento.Generation.Generation;

/// <summary>
/// Chunked, per-module generation with verification (ARCHITECTURE.md §8), the same for every provider:
/// <list type="number">
/// <item>Segment the transcript into chunks within the provider's budget (chapters and speaker turns as boundaries).</item>
/// <item>Map: for each module task and chunk, one request with the task's instructions and only the inputs it needs,
/// answered as schema-constrained JSON with citations (line number plus quoted words). A truncated answer is retried on
/// the two halves of its chunk, never parsed.</item>
/// <item>Repair citations and reduce in code: move a citation to the line that holds its quote, merge duplicates across
/// chunks, keep time order.</item>
/// <item>Verify each claim against only the span it cites (owner and due date as separate questions).</item>
/// <item>The grounding validator, then each module's content within its length, data modules placed without AI.</item>
/// </list>
/// The pipeline never writes files; the caller stores the rows and the record.
/// </summary>
public sealed class GenerationPipeline(ModuleCatalog catalog)
{
    /// <summary>Questions per cloud verification request.</summary>
    public const int VerifyBatchSize = 25;

    /// <summary>Questions per local verification request, within one module family (ENGINE-NOTES.md §I).</summary>
    public const int LocalVerifyBatchSize = 6;

    /// <summary>Merged copies tried in place of an unsupported claim.</summary>
    public const int MaxAlternates = 3;

    /// <summary>Why a statement is not in its module although nothing was wrong with it.</summary>
    public const string LeftOutForLength = "left out to keep the section within its length";

    public async Task<PipelineOutcome> RunAsync(PipelineInput input, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var warnings = new List<string>();
        await using var runner = new RequestRunner(input.Provider, observer: input.OnResponse, output: input.Output);
        var transcript = new TranscriptIndex(input.Payload.TranscriptLines);
        var clock = Stopwatch.StartNew();
        Report(progress, "composing", null, 2, "Preparing the inputs");

        // 1. Tasks and chunks.
        var tasks = Tasks(input);
        var chunks = input.Payload.TranscriptLines.Count == 0
            ? []
            : TranscriptChunker.Chunk(
                input.Payload.TranscriptLines,
                new ChunkOptions(Math.Max(64, input.ChunkTokens), new ProviderTokenCounter(input.Provider)),
                input.Material.ToPayloadInputs(null).Chapters);
        var composeMs = clock.ElapsedMilliseconds;
        if (chunks.Count > 0)
        {
            input.Output?.Step(
                GenerationOutputFeed.SegmentStep,
                "Segment the transcript",
                string.Create(CultureInfo.InvariantCulture, $"{Plural(chunks.Count, "segment")} of up to {Math.Max(64, input.ChunkTokens):N0} tokens, cut at chapters and speaker turns: {string.Join(", ", chunks.Select(c => $"{Timecode.Format(c.Start)}–{Timecode.Format(c.End)}"))}"),
                clock.Elapsed);
        }

        // 2. Map.
        clock.Restart();
        var claims = new List<Claim>();
        if (tasks.Count > 0 && chunks.Count > 0)
        {
            claims.AddRange(await MapAsync(input, tasks, chunks, runner, transcript, warnings, progress, cancellationToken));
        }

        // Agenda coverage also from the agenda items' own words in the transcript and in the other passes' citations.
        if (tasks.Any(t => t.Family == ModuleTask.AgendaCoverage))
        {
            var lines = chunks.SelectMany(c => c.Lines.Select(l => (Line: l, Chunk: c.Index))).GroupBy(l => l.Line.ShortId).Select(g => g.First()).ToList();
            claims.AddRange(AgendaMatcher.Candidates(input.Facts.Agenda, lines, claims));
        }

        var mapMs = clock.ElapsedMilliseconds;

        // 3. Reduce per family (citations were repaired as each answer was read).
        var reduceClock = Stopwatch.StartNew();
        var byFamily = claims.GroupBy(c => c.Family, StringComparer.Ordinal).ToDictionary(g => g.Key, g => ClaimReducer.Reduce(g).ToList(), StringComparer.Ordinal);
        if (claims.Count > 0)
        {
            input.Output?.Step(
                GenerationOutputFeed.ReduceStep,
                "Merge and repair in code",
                string.Create(CultureInfo.InvariantCulture, $"{Plural(claims.Count, "candidate")} from the map merged into {Plural(byFamily.Values.Sum(f => f.Count), "claim")} by citation and wording, in time order; each citation points at the line that holds its quote. The model is not involved."),
                reduceClock.Elapsed);
        }

        // 4. Verify; then, for a claim the span does not support, the copies merged into it (a wrong first copy must not
        // hide a right later one).
        clock.Restart();
        var unique = byFamily.Values.SelectMany(c => c).ToList();
        var unasked = await VerifyWithinLengthAsync(input, tasks, byFamily, transcript, runner, progress, cancellationToken);
        var unsupported = unique.Where(c => c.Verdict != Verdicts.Supported && c.Alternates.Count > 0 && !unasked.Contains(c)).ToList();
        if (unsupported.Count > 0)
        {
            await VerifyAsync(input, unsupported.SelectMany(c => c.Alternates.Take(MaxAlternates)).ToList(), transcript, runner, progress, cancellationToken);
            foreach (var claim in unsupported)
            {
                if (claim.Alternates.Take(MaxAlternates).FirstOrDefault(a => a.Verdict == Verdicts.Supported) is { } winner)
                {
                    winner.Notes.Add(string.Create(CultureInfo.InvariantCulture, $"used in place of a copy at line {claim.Line} that the transcript does not support"));
                    var family = byFamily[claim.Family];
                    family.Insert(family.IndexOf(claim) + 1, winner);
                    unique.Add(winner);
                }
            }
        }

        var verifyMs = clock.ElapsedMilliseconds;

        // 5. Validate and write.
        clock.Restart();
        Report(progress, "rendering", null, 92, "Checking every claim against the transcript");
        foreach (var claim in unique)
        {
            GroundingValidator.Validate(claim, transcript, input.Facts.People);
            if (unasked.Contains(claim))
            {
                claim.DropReason = LeftOutForLength;
            }
        }

        if (unique.Count > 0)
        {
            input.Output?.Step(
                GenerationOutputFeed.GroundingStep,
                "Check every claim against the transcript",
                string.Create(CultureInfo.InvariantCulture, $"{unique.Count(c => c.Kept)} of {Plural(unique.Count, "claim")} kept: each needs a citation to a real line and a supported verdict, and owners, dates and quotes must be in the transcript. The model is not involved."),
                clock.Elapsed);
        }

        var rows = new List<DocumentRow>();
        var modules = new List<RecordModule>();
        var records = new List<RecordClaim>();
        foreach (var row in input.Template.Rows.Where(r => r.Modules.Count > 0))
        {
            var blocks = new List<ModuleBlock>();
            foreach (var module in row.Modules)
            {
                var definition = catalog.Find(module.Type);
                var title = module.ResolveTitle(catalog);
                var data = DataModuleComposer.Compose(module, definition, input.Material);
                if (data is not null || definition is null)
                {
                    blocks.Add(Module(module, title, definition is { Source: ModuleSource.User } ? Provenance.FromUser() : Provenance.FromData(), data ?? [ModuleWriter.Note(ModuleWriter.NotDiscussed)]));
                    modules.Add(new RecordModule { ModuleId = module.Id, Type = module.Type, Source = definition?.Source == ModuleSource.User ? "user" : "data" });
                    continue;
                }

                var family = ModuleTask.FamilyOf(module);
                var candidates = (family is not null && byFamily.TryGetValue(family, out var list) ? list : []).Where(c => Reads(module, c)).ToList();
                var kept = Cap(module, candidates.Where(c => c.Kept).ToList());
                var copies = new List<Claim>();
                var number = 0;
                foreach (var candidate in candidates)
                {
                    var copy = candidate.Copy();
                    copy.Id = string.Create(CultureInfo.InvariantCulture, $"{module.Id}-c{++number}");
                    if (!kept.Contains(candidate) && copy.Kept)
                    {
                        copy.Kept = false;
                        copy.DropReason = LeftOutForLength;
                    }

                    copies.Add(copy);
                    records.Add(ToRecord(module, copy, transcript));
                }

                var shown = copies.Where(c => c.Kept).ToList();
                var content = ModuleWriter.Write(module, definition, shown, transcript, input.Facts.Agenda, input.Facts with { AgendaChecked = input.Selection.Agenda });
                var notDiscussed = shown.Count == 0 && module.Type != ModuleIds.Agenda && !(module.Type == ModuleIds.MeetingPurpose && input.Facts.Purpose is not null);
                blocks.Add(Module(module, title, Provenance.FromAi([.. shown.Select(c => c.Id)]), content));
                modules.Add(new RecordModule
                {
                    ModuleId = module.Id,
                    Type = module.Type,
                    Source = "ai",
                    Claims = copies.Count,
                    Verified = copies.Count(c => c.Verdict == Verdicts.Supported),
                    Dropped = copies.Count(c => !c.Kept),
                    NotDiscussed = notDiscussed,
                });
            }

            rows.Add(new DocumentRow { Modules = blocks });
        }

        Report(progress, "rendering", null, 98, "Writing the document");
        var timings = new RecordTimings { ComposeMs = composeMs, MapMs = mapMs, VerifyMs = verifyMs, WriteMs = clock.ElapsedMilliseconds, ModelLoadMs = runner.ModelLoadMs };
        return new PipelineOutcome(rows, modules, records, runner.Records, timings, chunks.Count, warnings);
    }

    /// <summary>The map tasks of a template, in template order: one per family, shared by the modules that read it.</summary>
    public static IReadOnlyList<ModuleTask> Tasks(PipelineInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var order = new List<string>();
        var modules = new Dictionary<string, List<TemplateModule>>(StringComparer.Ordinal);
        foreach (var module in input.Template.Modules())
        {
            if (ModuleTask.FamilyOf(module) is not { } family)
            {
                continue;
            }

            if (family == ModuleTask.AgendaCoverage && (!input.Selection.Agenda || input.Facts.Agenda.Count == 0))
            {
                continue;
            }

            if (module.Type == ModuleIds.MeetingPurpose && input.Facts.Purpose is not null)
            {
                continue;
            }

            if (!modules.TryGetValue(family, out var list))
            {
                modules[family] = list = [];
                order.Add(family);
            }

            list.Add(module);
        }

        return order.Select(f => new ModuleTask(f, modules[f])).ToList();
    }

    private async Task<List<Claim>> MapAsync(
        PipelineInput input,
        IReadOnlyList<ModuleTask> tasks,
        IReadOnlyList<TranscriptChunk> chunks,
        RequestRunner runner,
        TranscriptIndex transcript,
        List<string> warnings,
        IProgress<PipelineProgress>? progress,
        CancellationToken cancellationToken)
    {
        var claims = new List<Claim>();
        var work = tasks.SelectMany(t => chunks.Select(c => (Task: t, Chunk: c, Split: 0))).ToList();
        var total = work.Count;
        var finished = 0;
        for (var round = 0; work.Count > 0 && round < 2; round++)
        {
            var requests = work.Select(w => MapPrompts.Build(w.Task, input.Payload, w.Chunk, chunks.Count, catalog, input.MapOutputTokens, input.Bounded)).ToList();
            var done = finished;
            var batch = work;
            var batchTotal = total;
            var batchProgress = new InlineProgress<int>(n =>
            {
                // Read only what this batch captured.
                var index = Math.Clamp(n, 0, batch.Count - 1);
                Report(progress, "generating", batch[index].Task.Modules[0].Id, 5 + (55.0 * Math.Min(batchTotal, done + n) / Math.Max(1, batchTotal)), "Reading the transcript");
            });
            var passes = work.Select(w => new OutputPass(GenerationOutputFeed.MapStep, MapTitle(w.Task, w.Chunk, chunks.Count, w.Split > 0 ? " · half of it" : string.Empty, catalog))).ToList();
            var responses = await runner.RunAsync(requests, batchProgress, cancellationToken, passes);
            var retry = new List<(ModuleTask Task, TranscriptChunk Chunk, int Split)>();
            for (var i = 0; i < responses.Count; i++)
            {
                var (task, chunk, split) = work[i];
                var response = responses[i];
                if (response.StopReason == AiStopReason.MaxTokens && chunk.Lines.Count > 1 && split == 0)
                {
                    // A truncated answer is never parsed: read the two halves of the chunk instead.
                    var half = chunk.Lines.Count / 2;
                    retry.Add((task, Slice(chunk, 0, half), 1));
                    retry.Add((task, Slice(chunk, half, chunk.Lines.Count - half), 1));
                    total++;
                    continue;
                }

                finished++;
                if (response.StopReason != AiStopReason.Completed || response.Json is not { } json)
                {
                    warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{requests[i].Purpose}: the answer was incomplete ({response.ProviderStopReason ?? response.StopReason.ToString()}) and was not used."));
                    continue;
                }

                foreach (var claim in MapPrompts.Parse(task, json, chunk.Index))
                {
                    CitationRepair.Repair(claim, transcript, chunk);
                    claims.Add(claim);
                }
            }

            work = retry;
        }

        // A summary-like section that every chunk answered with nothing is read once more as a plain summary, without
        // its own instructions (MapPrompts.Build's plain); its points are verified like any other.
        var empty = tasks.Where(t => MapPrompts.ReadsAgainWhenEmpty(t) && !claims.Any(c => c.Family == t.Family)).ToList();
        if (empty.Count > 0 && chunks.Count > 0)
        {
            var again = empty.SelectMany(t => chunks.Select(c => (Task: t, Chunk: c))).ToList();
            Report(progress, "generating", again[0].Task.Modules[0].Id, 60, "Reading the transcript again");
            var requests = again.Select(w => MapPrompts.Build(w.Task, input.Payload, w.Chunk, chunks.Count, catalog, input.MapOutputTokens, input.Bounded, plain: true)).ToList();
            var passes = again.Select(w => new OutputPass(GenerationOutputFeed.MapStep, MapTitle(w.Task, w.Chunk, chunks.Count, " · read again as a plain summary", catalog))).ToList();
            var responses = await runner.RunAsync(requests, null, cancellationToken, passes);
            for (var i = 0; i < responses.Count; i++)
            {
                if (responses[i].StopReason != AiStopReason.Completed || responses[i].Json is not { } json)
                {
                    warnings.Add(string.Create(CultureInfo.InvariantCulture, $"{requests[i].Purpose}: the answer was incomplete ({responses[i].ProviderStopReason ?? responses[i].StopReason.ToString()}) and was not used."));
                    continue;
                }

                foreach (var claim in MapPrompts.Parse(again[i].Task, json, again[i].Chunk.Index))
                {
                    CitationRepair.Repair(claim, transcript, again[i].Chunk);
                    claims.Add(claim);
                }
            }
        }

        return claims;
    }

    private static async Task VerifyAsync(PipelineInput input, IReadOnlyList<Claim> claims, TranscriptIndex transcript, RequestRunner runner, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
    {
        var agenda = input.Facts.Agenda;
        var questions = claims.SelectMany(c => VerifyPrompts.Questions(c, n => n >= 1 && n <= agenda.Count ? agenda[n - 1].Text : null)).ToList();
        if (questions.Count == 0)
        {
            return;
        }

        Report(progress, "verifying", null, 60, "Checking each claim against the moment it cites");

        // A cloud model takes large numbered batches; the local model one batch per module family (claims of one kind,
        // at the size measured not to cost accuracy), or one question per request.
        var groups = input.VerifyBatch <= 1
            ? questions.Select(q => new[] { q }).ToList()
            : input.Bounded
                ? questions.GroupBy(q => q.Claim.Family, StringComparer.Ordinal).SelectMany(g => g.Chunk(input.VerifyBatch)).ToList()
                : questions.Chunk(input.VerifyBatch).ToList();
        var requests = groups.Select(g => input.VerifyBatch <= 1
            ? VerifyPrompts.ForQuestion(g[0], SpanOf(g[0], transcript))
            : VerifyPrompts.Batch(g.Select(q => (q, SpanOf(q, transcript))).ToList(), input.Bounded)).ToList();
        var passes = groups.Select(g => new OutputPass(GenerationOutputFeed.VerifyStep, VerifyTitle(g, input.Template))).ToList();
        var responses = await runner.RunAsync(requests, new InlineProgress<int>(n => Report(progress, "verifying", null, 60 + (30.0 * n / requests.Count), "Checking each claim against the moment it cites")), cancellationToken, passes);
        var answers = new Dictionary<VerifyQuestion, VerifyAnswer>();
        for (var r = 0; r < groups.Count; r++)
        {
            if (responses[r].StopReason != AiStopReason.Completed || responses[r].Json is not { } json)
            {
                continue;
            }

            if (input.VerifyBatch <= 1)
            {
                if (VerifyPrompts.ParseSingle(json) is { } answer)
                {
                    answers[groups[r][0]] = answer;
                }

                continue;
            }

            foreach (var (item, answer) in VerifyPrompts.ParseBatch(json))
            {
                if (item >= 1 && item <= groups[r].Length)
                {
                    answers[groups[r][item - 1]] = answer;
                }
            }
        }

        // Two votes on a borderline answer: "partly" where no shorter text can stand in (a decision, an action item, an
        // owner, a date, agenda coverage) is asked again as a plain yes/no over a wider span.
        var borderline = questions.Where(q => answers.GetValueOrDefault(q) is { Grade: VerifyAnswer.Partly } && !(q.Field == VerifyQuestion.ClaimField && q.Claim.Kind == ClaimKinds.Point)).ToList();
        if (borderline.Count > 0)
        {
            var votes = borderline.Select(_ => new OutputPass(GenerationOutputFeed.VerifyStep, "Second vote · one claim over a wider excerpt")).ToList();
            var second = await runner.RunAsync(borderline.Select(q => VerifyPrompts.SecondVote(q.Statement, WideSpanOf(q, transcript))).ToList(), null, cancellationToken, votes);
            for (var i = 0; i < borderline.Count; i++)
            {
                if (second[i].StopReason == AiStopReason.Completed && second[i].Json is { } json && VerifyPrompts.ParseSingle(json) is { IsSupported: true } vote)
                {
                    answers[borderline[i]] = new VerifyAnswer(VerifyAnswer.Supported, vote.Reason, null);
                }
            }
        }

        foreach (var question in questions)
        {
            Apply(question, answers.GetValueOrDefault(question), transcript);
        }
    }

    /// <summary>The second vote's span: one more line either side (two more after an agenda item's start).</summary>
    private static string WideSpanOf(VerifyQuestion question, TranscriptIndex transcript) =>
        question.Claim.Kind == ClaimKinds.Agenda ? transcript.Excerpt(question.Line, 1, 8) : transcript.Excerpt(question.Line, 3, 3);

    /// <summary>
    /// Records an answer on its claim. "Partly" keeps a summary point with the unsupported detail removed, when the
    /// shorter text adds no word that is neither the claim's nor the excerpt's; for every other kind (decisions, action
    /// items, owners, dates, agenda coverage, the next meeting) "partly" is not supported.
    /// </summary>
    internal static void Apply(VerifyQuestion question, VerifyAnswer? answer, TranscriptIndex transcript)
    {
        ArgumentNullException.ThrowIfNull(question);
        ArgumentNullException.ThrowIfNull(transcript);
        var claim = question.Claim;
        var verdict = answer is null ? Verdicts.NotChecked : answer.IsSupported ? Verdicts.Supported : Verdicts.Unsupported;
        switch (question.Field)
        {
            case VerifyQuestion.OwnerField:
                claim.OwnerVerdict = verdict;
                return;
            case VerifyQuestion.DueField:
                claim.DueVerdict = verdict;
                return;
        }

        if (answer is { Grade: VerifyAnswer.Partly } && Trimmed(claim, answer.SupportedPart, SpanOf(question, transcript)) is { } shorter)
        {
            claim.Notes.Add("shortened to what the cited moment supports");
            claim.Text = shorter;
            verdict = Verdicts.Supported;
        }

        claim.Verdict = verdict;
        claim.Reason = answer?.Reason;
    }

    /// <summary>The supported part of a summary point, or <c>null</c> when it cannot stand in for the claim.</summary>
    internal static string? Trimmed(Claim claim, string? part, string excerpt)
    {
        ArgumentNullException.ThrowIfNull(claim);
        if (claim.Kind != ClaimKinds.Point || string.IsNullOrWhiteSpace(part) || TextMatch.Normalize(part) == TextMatch.Normalize(claim.Text))
        {
            return null;
        }

        var words = TextMatch.Words(part);
        var allowed = TextMatch.Words(claim.Text);
        allowed.UnionWith(TextMatch.Words(excerpt));
        return words.Count >= 2 && words.All(allowed.Contains) ? part.Trim() : null;
    }

    /// <summary>
    /// The span the verifier reads: two lines either side of the cited one; for an agenda item, the line where its
    /// discussion starts and the six after it (a topic is discussed over several turns).
    /// </summary>
    private static string SpanOf(VerifyQuestion question, TranscriptIndex transcript) =>
        question.Claim.Kind == ClaimKinds.Agenda ? transcript.Excerpt(question.Line, 1, 6) : transcript.Excerpt(question.Line);

    private static bool Reads(TemplateModule module, Claim claim) => module.Type switch
    {
        ModuleIds.Decisions => claim.Kind == ClaimKinds.Decision,
        ModuleIds.ActionItems or ModuleIds.Owner or ModuleIds.Deadline => claim.Kind == ClaimKinds.Action,
        _ => true,
    };

    /// <summary>The kept claims within the module's length, spread evenly over the recording when there are more.</summary>
    private static List<Claim> Cap(TemplateModule module, List<Claim> kept) => Spread(kept, CapOf(module));

    /// <summary>How many statements a module shows (the meeting purpose is one line).</summary>
    internal static int CapOf(TemplateModule module) => module.Type switch
    {
        ModuleIds.Decisions or ModuleIds.ActionItems or ModuleIds.Owner or ModuleIds.Deadline or ModuleIds.FollowUpEmail => ModuleTask.Cap(module.Length) * 4,
        ModuleIds.Agenda => int.MaxValue,
        ModuleIds.Quote => module.Length switch { ModuleLength.Short => 1, ModuleLength.Long => 4, _ => 2 },
        ModuleIds.MeetingPurpose => 1,
        _ => ModuleTask.Cap(module.Length),
    };

    /// <summary>At most <paramref name="count"/> of the claims, spread evenly over the recording.</summary>
    internal static List<Claim> Spread(IReadOnlyList<Claim> claims, int count)
    {
        if (claims.Count <= count)
        {
            return [.. claims];
        }

        var step = (double)claims.Count / count;
        return Enumerable.Range(0, count).Select(i => claims[(int)Math.Floor(i * step)]).ToList();
    }

    /// <summary>
    /// Verifies every claim a module can show. A summary-like module (one map pass per module) shows at most its length's
    /// worth of statements, so only that many are checked first, spread over the recording, and then only as many more
    /// as replace the ones the transcript does not support (two rounds). The rest are never asked about and are left out
    /// for length. Decisions, action items, the agenda and the next meeting are always checked in full.
    /// </summary>
    /// <returns>The claims left unchecked.</returns>
    private static async Task<HashSet<Claim>> VerifyWithinLengthAsync(PipelineInput input, IReadOnlyList<ModuleTask> tasks, Dictionary<string, List<Claim>> byFamily, TranscriptIndex transcript, RequestRunner runner, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
    {
        var first = new List<Claim>();
        var waiting = new Dictionary<string, (int Cap, List<Claim> Unasked)>(StringComparer.Ordinal);
        foreach (var (family, claims) in byFamily)
        {
            var task = tasks.FirstOrDefault(t => t.Family == family);
            var cap = task is { IsPoints: true } && task.Modules.Count == 1 ? CapOf(task.Modules[0]) : int.MaxValue;
            if (claims.Count <= cap)
            {
                first.AddRange(claims);
                continue;
            }

            var chosen = Spread(claims, cap);
            first.AddRange(chosen);
            waiting[family] = (cap, claims.Where(c => !chosen.Contains(c)).ToList());
        }

        await VerifyAsync(input, first, transcript, runner, progress, cancellationToken);
        for (var round = 0; round < 2 && waiting.Count > 0; round++)
        {
            var more = new List<Claim>();
            foreach (var (family, (cap, unasked)) in waiting)
            {
                var missing = cap - byFamily[family].Count(c => c.Verdict == Verdicts.Supported);
                if (missing > 0 && unasked.Count > 0)
                {
                    var next = Spread(unasked, missing);
                    unasked.RemoveAll(next.Contains);
                    more.AddRange(next);
                }
            }

            if (more.Count == 0)
            {
                break;
            }

            await VerifyAsync(input, more, transcript, runner, progress, cancellationToken);
        }

        return waiting.Values.SelectMany(w => w.Unasked).ToHashSet();
    }

    private static ModuleBlock Module(TemplateModule module, string title, Provenance provenance, IReadOnlyList<Memento.Documents.Model.Blocks.Block> blocks) => new()
    {
        Id = module.Id,
        Type = module.Type,
        Title = title,
        TextSize = module.TextSize,
        LinkToTranscript = module.LinkToTranscript,
        Provenance = provenance,
        Blocks = blocks,
    };

    private static RecordClaim ToRecord(TemplateModule module, Claim claim, TranscriptIndex transcript)
    {
        var cited = transcript.Find(claim.Line);
        var notes = claim.Notes.ToList();
        if (claim.DropReason is { } reason)
        {
            notes.Insert(0, reason);
        }

        return new RecordClaim
        {
            Id = claim.Id,
            ModuleId = module.Id,
            Kind = claim.Kind,
            Text = claim.Kind == ClaimKinds.Agenda ? string.Create(CultureInfo.InvariantCulture, $"Agenda item {claim.AgendaItem} discussed") : claim.Text,
            SegmentId = cited?.SegmentId,
            T = cited?.Start,
            Quote = claim.Quote,
            Owner = claim.Owner,
            Due = claim.Due,
            Verdict = claim.Verdict,
            Reason = claim.Reason,
            OwnerVerdict = claim.OwnerVerdict,
            DueVerdict = claim.DueVerdict,
            Kept = claim.Kept,
            Note = notes.Count == 0 ? null : string.Join("; ", notes),
        };
    }

    private static TranscriptChunk Slice(TranscriptChunk chunk, int start, int count)
    {
        var lines = chunk.Lines.Skip(start).Take(count).ToList();
        return new TranscriptChunk(
            chunk.Index,
            lines,
            lines[0].Start,
            lines.Max(l => l.End),
            lines.Select(l => l.SpeakerId).OfType<string>().Distinct(StringComparer.Ordinal).ToList(),
            chunk.Tokens * count / Math.Max(1, chunk.Lines.Count),
            string.Join('\n', lines.Select(l => l.Rendered)),
            ChunkBoundary.Segment);
    }

    /// <summary>"Decisions and action items · segment 1 of 2" for the Live output list.</summary>
    internal static string MapTitle(ModuleTask task, TranscriptChunk chunk, int chunkCount, string suffix, ModuleCatalog catalog) =>
        string.Create(CultureInfo.InvariantCulture, $"{FamilyTitle(task.Family, task.Modules, catalog)} · segment {chunk.Index + 1} of {chunkCount}{suffix}");

    /// <summary>"Check Decisions and action items · 6 claims", or "Check 25 claims" for a cloud batch of several kinds.</summary>
    internal static string VerifyTitle(IReadOnlyList<VerifyQuestion> group, DocumentTemplate template)
    {
        ArgumentNullException.ThrowIfNull(group);
        ArgumentNullException.ThrowIfNull(template);
        var families = group.Select(q => q.Claim.Family).Distinct(StringComparer.Ordinal).ToList();
        var count = group.Count == 1 ? "one question" : string.Create(CultureInfo.InvariantCulture, $"{group.Count} questions");
        return families.Count == 1
            ? $"Check {FamilyTitle(families[0], template.Modules().Where(m => ModuleTask.FamilyOf(m) == families[0]).ToList(), ModuleCatalog.Default)} · {count}"
            : $"Check {count}";
    }

    /// <summary>What a map family reads, in the words of the modules it serves.</summary>
    private static string FamilyTitle(string family, IReadOnlyList<TemplateModule> modules, ModuleCatalog catalog) => family switch
    {
        ModuleTask.Commitments => "Decisions and action items",
        ModuleTask.AgendaCoverage => "Agenda",
        ModuleTask.Quotes => "Quotes",
        ModuleTask.NextMeeting => "Next meeting",
        _ => modules.Count > 0 ? modules[0].ResolveTitle(catalog) : family,
    };

    private static string Plural(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count:N0} {noun}{(count == 1 ? string.Empty : "s")}");

    private static void Report(IProgress<PipelineProgress>? progress, string stage, string? moduleId, double percent, string? message) =>
        progress?.Report(new PipelineProgress(stage, moduleId, Math.Round(Math.Clamp(percent, 0, 100), 1), message));
}
