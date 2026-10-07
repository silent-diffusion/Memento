using System.Collections.Concurrent;
using System.Diagnostics;
using Memento.Documents.Agenda;

namespace Memento.Documents.Tests.Support;

/// <summary>Parses each fixture once per test run, so the per-fixture tests and the report share the (OCR) work.</summary>
internal static class FixtureRunner
{
    private static readonly AgendaImporter Importer = AgendaImporter.CreateDefault();
    private static readonly ConcurrentDictionary<string, Lazy<Task<FixtureOutcome>>> Outcomes = new(StringComparer.Ordinal);

    public static Task<FixtureOutcome> RunAsync(string name) =>
        Outcomes.GetOrAdd(name, n => new Lazy<Task<FixtureOutcome>>(() => ParseAsync(n))).Value;

    private static async Task<FixtureOutcome> ParseAsync(string name)
    {
        var path = FixturePaths.Output(name);
        var expected = ExpectedAgenda.Load(path + ".expected");
        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = FixturePaths.IsPasted(name)
                ? await Importer.ParseTextAsync(await File.ReadAllTextAsync(path), null, CancellationToken.None)
                : await Importer.ImportFileAsync(path, null, CancellationToken.None);
            return new FixtureOutcome(name, expected, result, null, Stopwatch.GetElapsedTime(started));
        }
        catch (Exception e) when (e is AgendaImportException or IOException)
        {
            return new FixtureOutcome(name, expected, null, e, Stopwatch.GetElapsedTime(started));
        }
    }
}
