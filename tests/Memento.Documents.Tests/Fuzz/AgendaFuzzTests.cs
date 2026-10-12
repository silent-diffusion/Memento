using System.Globalization;
using Memento.Documents.Tests.Support;
using Xunit.Abstractions;

namespace Memento.Documents.Tests.Fuzz;

/// <summary>
/// Seeded, deterministic fuzzing of every agenda parser and of the viewer's HTML reader: each fixture is mutated
/// (bit flips, truncation, duplicated chunks, inserted bytes; for Word and Excel also unzip → change the XML → zip,
/// with bad attribute values, deep nesting and DTDs) and parsed. Only <c>AgendaImportException</c> may escape a parser
/// (nothing may escape <c>HtmlToBlocks</c>), and no single run may do more work than <see cref="MaxRun"/> (<see cref="Work.Budget"/> of 5 s, in
/// processor cycles: a stalled CI runner measured a 0.5 s parse at 24 s on a stopwatch).
/// <para>
/// Results when the harness was added (500 runs per target, fixed seeds, Release; the whole class about 40 s):
/// before the fixes, a deeply nested Word or Excel part overflowed the stack and killed the test host; XLSX let
/// XmlException (207), FormatException (19), InvalidOperationException (31), ArgumentOutOfRangeException (30),
/// InvalidDataException (10) and OpenXmlPackageException (1) escape, and a row r="2147483648" grew the host past 28 GB;
/// DOCX let XmlException (192), FormatException (27), InvalidOperationException (29), InvalidDataException (7) and
/// OverflowException (1) escape; PDF let a FormatException escape (a non-ASCII digit parsed as a list number). Text,
/// Markdown, CSV/TSV, pasted text, images (fake OCR) and HTML had no escapes. After the fixes no target lets anything
/// but AgendaImportException escape; slowest single runs: DOCX 0.6 s, PDF 1.0 s, XLSX 0.2 s, the rest under 0.15 s.
/// </para>
/// </summary>
public sealed class AgendaFuzzTests(ITestOutputHelper output)
{
    public const int Iterations = 500;

    private static readonly long MaxRun = Work.Budget(5);

    public static TheoryData<string> Targets => new(FuzzTargets.Names);

    [Theory]
    [MemberData(nameof(Targets))]
    public async Task MutatedInputsFailOnlyWithAnAgendaError(string name)
    {
        var target = FuzzTargets.Get(name);
        var seeds = target.Fixtures.Select(f => (Name: f, Bytes: Agendas.Fixture(f))).ToList();
        var random = new Random(target.Seed);
        var failures = new List<string>();
        var escaped = new Dictionary<string, int>(StringComparer.Ordinal);
        var slowest = 0L;
        var parsed = 0;
        var refused = 0;
        for (var i = 0; i < Iterations; i++)
        {
            var seed = seeds[i % seeds.Count];
            var (input, description) = target.Mutate(seed.Bytes, random);
            var (cycles, error) = await Work.OfRunAsync(() => target.Run(input, seed.Name, CancellationToken.None));
            if (error is null)
            {
                parsed++;
            }
            else if (FuzzTargets.IsAllowed(name, error))
            {
                refused++;
            }
            else
            {
                var type = error.GetType().Name;
                escaped[type] = escaped.GetValueOrDefault(type) + 1;
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"#{i} {seed.Name} ({description}): {type}: {error.Message}"));
            }

            slowest = Math.Max(slowest, cycles);
            if (cycles > MaxRun)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"#{i} {seed.Name} ({description}): {cycles / 1e9:0.0} G cycles of work"));
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: {Iterations} runs, {parsed} parsed, {refused} refused with an agenda error, costliest {slowest / 1e9:0.00} G cycles, escaped: {(escaped.Count == 0 ? "none" : string.Join(", ", escaped.Select(e => $"{e.Key} x{e.Value}")))}"));
        Assert.True(failures.Count == 0, $"{failures.Count} of {Iterations} runs failed:\n" + string.Join('\n', failures.Take(25)));
    }
}
