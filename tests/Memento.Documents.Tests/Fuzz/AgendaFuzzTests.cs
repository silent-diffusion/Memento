using System.Diagnostics;
using System.Globalization;
using Memento.Documents.Tests.Support;
using Xunit.Abstractions;

namespace Memento.Documents.Tests.Fuzz;

/// <summary>
/// Seeded, deterministic fuzzing of every agenda parser and of the viewer's HTML reader: each fixture is mutated
/// (bit flips, truncation, duplicated chunks, inserted bytes; for Word and Excel also unzip → change the XML → zip,
/// with bad attribute values, deep nesting and DTDs) and parsed. Only <c>AgendaImportException</c> may escape a parser
/// (nothing may escape <c>HtmlToBlocks</c>), and no single run may take longer than <see cref="MaxRun"/>.
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

    private static readonly TimeSpan MaxRun = TimeSpan.FromSeconds(5);

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
        var slowest = TimeSpan.Zero;
        var parsed = 0;
        var refused = 0;
        for (var i = 0; i < Iterations; i++)
        {
            var seed = seeds[i % seeds.Count];
            var (input, description) = target.Mutate(seed.Bytes, random);
            var watch = Stopwatch.StartNew();
            try
            {
                await target.Run(input, seed.Name, CancellationToken.None);
                parsed++;
            }
            catch (Exception e) when (FuzzTargets.IsAllowed(name, e))
            {
                refused++;
            }
            catch (Exception e)
            {
                var type = e.GetType().Name;
                escaped[type] = escaped.GetValueOrDefault(type) + 1;
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"#{i} {seed.Name} ({description}): {type}: {e.Message}"));
            }

            watch.Stop();
            slowest = watch.Elapsed > slowest ? watch.Elapsed : slowest;
            if (watch.Elapsed > MaxRun)
            {
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"#{i} {seed.Name} ({description}): took {watch.Elapsed.TotalSeconds:0.0} s"));
            }
        }

        output.WriteLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{name}: {Iterations} runs, {parsed} parsed, {refused} refused with an agenda error, slowest {slowest.TotalMilliseconds:0} ms, escaped: {(escaped.Count == 0 ? "none" : string.Join(", ", escaped.Select(e => $"{e.Key} x{e.Value}")))}"));
        Assert.True(failures.Count == 0, $"{failures.Count} of {Iterations} runs failed:\n" + string.Join('\n', failures.Take(25)));
    }
}
