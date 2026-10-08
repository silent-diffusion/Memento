using System.Text.Json;
using Memento.AI.Local;
using Memento.AI.Tests.Fakes;

namespace Memento.AI.Tests.Local;

/// <summary>
/// The worker streams each answer as it is decoded (the Live output sheet): coalesced <c>generating</c> lines that add up
/// to the answer exactly, then an <c>answered</c> line per prompt; grammar-constrained prompts stream their raw tokens too.
/// No wall clock: the coalescer reads a <see cref="ManualClock"/>.
/// </summary>
public sealed class LocalTokenStreamingTests : IDisposable
{
    private static readonly string[] Letters = [.. Enumerable.Range(0, 26).Select(i => ((char)('a' + i)).ToString())];

    private readonly string _folder = Directory.CreateTempSubdirectory("memento-ai-stream-").FullName;
    private readonly FakeLocalEngineFactory _engines = new();
    private readonly LocalModelEntry _model;
    private readonly string _modelPath;

    public LocalTokenStreamingTests()
    {
        _modelPath = Path.Combine(_folder, "model.gguf");
        File.WriteAllBytes(_modelPath, [1, 2, 3, 4]);
        _model = LocalModelCatalog.Find(LocalModelCatalog.Ministral3ThreeB)! with { SizeBytes = 4 };
    }

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    [Fact]
    public void WhileTimeStandsStillTheFirstPieceGoesAtOnceThenEveryEighth()
    {
        var clock = new ManualClock();
        var lines = new List<(string Text, int Pieces)>();
        var stream = new LocalTokenCoalescer(clock, (text, pieces, _) => lines.Add((text, pieces)));

        foreach (var letter in Letters)
        {
            stream.Add(letter);
        }

        stream.Flush();
        stream.Flush();

        Assert.Equal([1, 9, 17, 25, 26], lines.Select(l => l.Pieces));
        Assert.Equal(string.Concat(Letters), string.Concat(lines.Select(l => l.Text)));
        Assert.Equal(26, stream.Pieces);
        Assert.Equal(5, stream.Lines);
    }

    [Fact]
    public void PiecesTenMillisecondsApartGoOutEveryFortyMilliseconds()
    {
        var clock = new ManualClock();
        var lines = new List<(string Text, int Pieces, TimeSpan Elapsed)>();
        var stream = new LocalTokenCoalescer(clock, (text, pieces, elapsed) => lines.Add((text, pieces, elapsed)));

        foreach (var letter in Letters)
        {
            clock.Advance(TimeSpan.FromMilliseconds(10));
            stream.Add(letter);
        }

        stream.Flush();

        Assert.Equal([1, 5, 9, 13, 17, 21, 25, 26], lines.Select(l => l.Pieces));
        Assert.Equal("bcde", lines[1].Text);
        Assert.Equal(TimeSpan.FromMilliseconds(240), lines[^2].Elapsed);
        Assert.Equal(string.Concat(Letters), string.Concat(lines.Select(l => l.Text)));
    }

    [Fact]
    public void SlowPiecesEachGetTheirOwnLineAndEmptyPiecesNone()
    {
        var clock = new ManualClock();
        var lines = new List<string>();
        var stream = new LocalTokenCoalescer(clock, (text, _, _) => lines.Add(text));

        stream.Add(string.Empty);
        foreach (var letter in Letters.Take(5))
        {
            clock.Advance(TimeSpan.FromMilliseconds(60));
            stream.Add(letter);
        }

        stream.Flush();

        Assert.Equal(["a", "b", "c", "d", "e"], lines);
    }

    [Fact]
    public async Task TheWorkerProtocolStreamsEachAnswerInFewLinesAndClosesItWithAnAnsweredLine()
    {
        var clock = new ManualClock();
        var runner = new LocalLlmJobRunner(_engines, new SpyLogger<LocalLlmJobRunner>(), clock);
        var words = string.Join(' ', Enumerable.Range(1, 20).Select(i => "w" + i));
        _engines.Answer = p => (p.Purpose == "map.grammar" ? "{\"items\": [\"" + words + "\"]}" : words, LocalLlmStopReasons.EndOfGeneration);
        var job = Job() with
        {
            Prompts =
            [
                new LocalLlmPrompt("map.plain", "s", [new LocalLlmTurn(LocalLlmTurn.User, "u")], 100),
                new LocalLlmPrompt("map.grammar", "s", [new LocalLlmTurn(LocalLlmTurn.User, "u")], 100, "root ::= \"{\" [^}]* \"}\""),
            ],
        };
        var start = JsonSerializer.Serialize(new LocalLlmWorkerCommand("start", new LocalLlmWorkerJob(LocalLlmWorkerJob.LlmKind, job)), LocalLlmJsonContext.Default.LocalLlmWorkerCommand);
        using var output = new StringWriter();

        var exit = await runner.RunJsonLinesAsync(new HeldInput(start), output);

        Assert.Equal(LocalLlmJobRunner.ExitOk, exit);
        var progress = output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(l => JsonSerializer.Deserialize(l, LocalLlmJsonContext.Default.LocalLlmWorkerReply)!)
            .Where(r => r.LlmProgress is { PromptIndex: >= 0 })
            .Select(r => r.LlmProgress!)
            .ToList();
        for (var index = 0; index < 2; index++)
        {
            var mine = progress.Where(p => p.PromptIndex == index).ToList();
            var generating = mine.Where(p => p.Phase == LocalLlmProgress.Generating).ToList();
            var expected = _engines.Answer(job.Prompts[index]).Text;
            Assert.Equal(LocalLlmProgress.ReadingPrompt, mine[0].Phase);
            Assert.Equal(expected, string.Concat(generating.Select(p => p.Delta)));
            // While time stands still: the first piece, every eighth after it, then the rest when the answer ends.
            var pieces = expected.Split(' ').Length;
            var marks = new List<int> { 1 };
            for (var k = 9; k <= pieces; k += 8)
            {
                marks.Add(k);
            }

            if (marks[^1] != pieces)
            {
                marks.Add(pieces);
            }

            Assert.Equal(marks, generating.Select(p => p.OutputTokens));
            Assert.All(generating, p => Assert.NotNull(p.ElapsedMs));
            var answered = mine[^1];
            Assert.Equal(LocalLlmProgress.Answered, answered.Phase);
            Assert.Equal(pieces, answered.OutputTokens);
            Assert.Equal(LocalLlmStopReasons.EndOfGeneration, answered.StopReason);
            Assert.Equal(2, answered.PromptTokens);
            Assert.Equal(20, answered.ElapsedMs);
            Assert.Null(answered.Delta);
        }

        // Prompt 0 is closed before prompt 1 is read.
        Assert.True(progress.FindIndex(p => p is { PromptIndex: 0, Phase: LocalLlmProgress.Answered }) < progress.FindIndex(p => p is { PromptIndex: 1, Phase: LocalLlmProgress.ReadingPrompt }));
        Assert.Contains(_engines.Prompts, p => p.Grammar is not null);
    }

    [Fact]
    public async Task TheProviderReportsEachAnswerWhenItIsCompleteWithItsFacts()
    {
        var provider = new LocalAiProvider(_model, _modelPath, new InProcessLocalLlmJobClient(new LocalLlmJobRunner(_engines, new SpyLogger<LocalLlmJobRunner>(), new ManualClock())), EstimatingTokenCounter.Generic, () => 5L << 30);
        _engines.Answer = p => ("answer to " + p.Purpose, LocalLlmStopReasons.EndOfGeneration);
        var progress = new ProgressLog<AiProgress>();
        var requests = Enumerable.Range(0, 2).Select(i => AiRequest.Create($"map.chunk{i}", "Extract.", $"Chunk {i}.", 100)).ToList();

        var responses = await provider.GenerateManyAsync(requests, progress, CancellationToken.None);

        for (var i = 0; i < 2; i++)
        {
            var mine = progress.Items.Where(p => p.Index == i).ToList();
            Assert.Equal(responses[i].Text, string.Concat(mine.Where(p => p.Stage == AiProgressStage.Generating).Select(p => p.Delta)));
            var answered = Assert.Single(mine, p => p.Stage == AiProgressStage.Answered);
            // The fake engine writes for 20 ms: (tokens - 1) per 0.02 s.
            Assert.Equal(new AiAnswerFacts("eog", responses[i].Usage.InputTokens, Math.Round((responses[i].Usage.OutputTokens - 1) / 0.02, 1)), answered.Answer);
            Assert.Equal(responses[i].Usage.OutputTokens, answered.OutputTokens);
            Assert.Equal(TimeSpan.FromMilliseconds(20), answered.Elapsed);
            Assert.Same(mine[^1], answered);
        }

        Assert.Equal(AiProgressStage.Done, progress.Items[^1].Stage);
    }

    /// <summary>The host's side of the pipe: the start line, then nothing until the job ends (the end of input would cancel it).</summary>
    private sealed class HeldInput(string start) : TextReader
    {
        private bool _sent;

        public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
        {
            if (!_sent)
            {
                _sent = true;
                return start;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return null;
        }
    }

    private LocalLlmJob Job() => new()
    {
        ModelPath = _modelPath,
        ModelId = _model.Id,
        ModelName = _model.Name,
        Profile = _model.Llm,
        Prompts = [new LocalLlmPrompt("t", "s", [new LocalLlmTurn(LocalLlmTurn.User, "u")], 10)],
    };
}
