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

    /// <summary>Merged copies tried in place of an unsupported claim.</summary>
    public const int MaxAlternates = 3;

    public async Task<PipelineOutcome> RunAsync(PipelineInput input, IProgress<PipelineProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        var warnings = new List<string>();
        var runner = new RequestRunner(input.Provider);
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

        // 2. Map.
        clock.Restart();
        var claims = new List<Claim>();
        if (tasks.Count > 0 && chunks.Count > 0)
        {
            claims.AddRange(await MapAsync(input, tasks, chunks, runner, transcript, warnings, progress, cancellationToken));
        }

        var mapMs = clock.ElapsedMilliseconds;

        // 3. Reduce per family (citations were repaired as each answer was read).
        var byFamily = claims.GroupBy(c => c.Family, StringComparer.Ordinal).ToDictionary(g => g.Key, g => ClaimReducer.Reduce(g).ToList(), StringComparer.Ordinal);

        // 4. Verify; then, for a claim the span does not support, the copies merged into it (a wrong first copy must not
        // hide a right later one).
        clock.Restart();
        var unique = byFamily.Values.SelectMany(c => c).ToList();
        await VerifyAsync(input, unique, transcript, runner, progress, cancellationToken);
        var unsupported = unique.Where(c => c.Verdict != Verdicts.Supported && c.Alternates.Count > 0).ToList();
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
                        copy.DropReason = "left out to keep the section within its length";
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
            var batchProgress = new Progress<int>(n =>
            {
                var index = Math.Min(n, work.Count - 1);
                Report(progress, "generating", work[index].Task.Modules[0].Id, 5 + (55.0 * Math.Min(total, done + n) / Math.Max(1, total)), "Reading the transcript");
            });
            var responses = await runner.RunAsync(requests, batchProgress, cancellationToken);
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
        var verdicts = new Dictionary<VerifyQuestion, (bool Supported, string? Reason)>();
        if (input.BatchVerify)
        {
            var batches = questions.Chunk(VerifyBatchSize).ToList();
            var requests = batches.Select(b => VerifyPrompts.Batch(b.Select(q => (q, transcript.Excerpt(q.Line))).ToList())).ToList();
            var responses = await runner.RunAsync(requests, new Progress<int>(n => Report(progress, "verifying", null, 60 + (30.0 * n / requests.Count), "Checking each claim against the moment it cites")), cancellationToken);
            for (var b = 0; b < batches.Count; b++)
            {
                if (responses[b].StopReason == AiStopReason.Completed && responses[b].Json is { } json)
                {
                    foreach (var (item, verdict) in VerifyPrompts.ParseBatch(json))
                    {
                        if (item >= 1 && item <= batches[b].Length)
                        {
                            verdicts[batches[b][item - 1]] = verdict;
                        }
                    }
                }
            }
        }
        else
        {
            var requests = questions.Select(q => VerifyPrompts.ForQuestion(q, transcript.Excerpt(q.Line))).ToList();
            var responses = await runner.RunAsync(requests, new Progress<int>(n => Report(progress, "verifying", null, 60 + (30.0 * n / requests.Count), "Checking each claim against the moment it cites")), cancellationToken);
            for (var i = 0; i < responses.Count; i++)
            {
                if (responses[i].StopReason == AiStopReason.Completed && responses[i].Json is { } json && VerifyPrompts.ParseSingle(json) is { Supported: { } supported } verdict)
                {
                    verdicts[questions[i]] = (supported, verdict.Reason);
                }
            }
        }

        foreach (var question in questions)
        {
            var verdict = verdicts.TryGetValue(question, out var v) ? (v.Supported ? Verdicts.Supported : Verdicts.Unsupported) : Verdicts.NotChecked;
            switch (question.Field)
            {
                case VerifyQuestion.OwnerField:
                    question.Claim.OwnerVerdict = verdict;
                    break;
                case VerifyQuestion.DueField:
                    question.Claim.DueVerdict = verdict;
                    break;
                default:
                    question.Claim.Verdict = verdict;
                    question.Claim.Reason = v.Reason;
                    break;
            }
        }
    }

    private static bool Reads(TemplateModule module, Claim claim) => module.Type switch
    {
        ModuleIds.Decisions => claim.Kind == ClaimKinds.Decision,
        ModuleIds.ActionItems or ModuleIds.Owner or ModuleIds.Deadline => claim.Kind == ClaimKinds.Action,
        _ => true,
    };

    /// <summary>The kept claims within the module's length, spread evenly over the recording when there are more.</summary>
    private static List<Claim> Cap(TemplateModule module, List<Claim> kept)
    {
        var cap = module.Type switch
        {
            ModuleIds.Decisions or ModuleIds.ActionItems or ModuleIds.Owner or ModuleIds.Deadline or ModuleIds.FollowUpEmail => ModuleTask.Cap(module.Length) * 4,
            ModuleIds.Agenda => int.MaxValue,
            ModuleIds.Quote => module.Length switch { ModuleLength.Short => 1, ModuleLength.Long => 4, _ => 2 },
            _ => ModuleTask.Cap(module.Length),
        };
        if (kept.Count <= cap)
        {
            return kept;
        }

        var step = (double)kept.Count / cap;
        return Enumerable.Range(0, cap).Select(i => kept[(int)Math.Floor(i * step)]).ToList();
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

    private static void Report(IProgress<PipelineProgress>? progress, string stage, string? moduleId, double percent, string? message) =>
        progress?.Report(new PipelineProgress(stage, moduleId, Math.Round(Math.Clamp(percent, 0, 100), 1), message));
}
