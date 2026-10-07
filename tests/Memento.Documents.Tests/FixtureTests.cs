using Memento.Documents.Agenda;
using Memento.Documents.Tests.Support;
using Xunit.Abstractions;

namespace Memento.Documents.Tests;

/// <summary>Every agenda fixture parses to its expected items (ROADMAP M3 acceptance).</summary>
public sealed class FixtureTests(ITestOutputHelper output)
{
    public static TheoryData<string> DocumentFixtures => new(FixturePaths.All().Where(n => !FixturePaths.IsImage(n)));

    public static TheoryData<string> ImageFixtures => new(FixturePaths.All().Where(FixturePaths.IsImage));

    [Theory]
    [MemberData(nameof(DocumentFixtures))]
    public async Task DocumentFixtureParsesToItsExpectedItems(string name)
    {
        var outcome = await FixtureRunner.RunAsync(name);
        output.WriteLine(outcome.Summary);
        Assert.Null(outcome.Error);
        var result = outcome.Result!;
        var expected = outcome.Expected;

        Assert.True(result.ParsedLocally);
        Assert.Equal(expected.Source, result.Source.ToString());
        Assert.Equal(expected.Title, result.Title);
        Assert.Equal(
            string.Join('\n', expected.Items.Select(i => i.ToString())),
            string.Join('\n', outcome.Actual.Select(i => i.ToString())));
        foreach (var code in expected.Warnings)
        {
            Assert.Contains(result.Warnings, w => w.Code == code);
        }

        Assert.All(result.Items.Where(i => i.Uncertain), i => Assert.False(string.IsNullOrWhiteSpace(i.UncertainReason)));
    }

    [OcrTheory]
    [MemberData(nameof(ImageFixtures))]
    public async Task ImageFixtureIsReadByTextRecognition(string name)
    {
        var outcome = await FixtureRunner.RunAsync(name);
        output.WriteLine(outcome.Summary);
        foreach (var item in outcome.Actual)
        {
            output.WriteLine("  " + item);
        }

        Assert.Null(outcome.Error);
        var result = outcome.Result!;
        Assert.Equal(AgendaSourceKind.Image, result.Source);
        Assert.Equal("windows", result.OcrEngine);
        Assert.Contains(result.Warnings, w => w.Code == AgendaWarningCodes.OcrReview);
        Assert.InRange(result.Items.Count, outcome.Expected.Items.Count - 1, outcome.Expected.Items.Count + 1);
        Assert.InRange(outcome.CharacterErrorRate, 0, outcome.Expected.MaxCharacterErrorRate ?? 0.05);
    }

    [Fact]
    public void EveryFormatHasAtLeastTwoFixtures()
    {
        var byKind = FixturePaths.All()
            .GroupBy(n => FixturePaths.IsPasted(n) ? "paste" : Path.GetExtension(n).ToLowerInvariant() switch
            {
                ".jpg" or ".png" => "image",
                var other => other,
            })
            .ToDictionary(g => g.Key, g => g.Count());
        foreach (var kind in new[] { ".txt", "paste", ".md", ".csv", ".docx", ".xlsx", ".pdf", "image" })
        {
            Assert.True(byKind.TryGetValue(kind, out var count) && count >= 2, $"Fewer than two {kind} fixtures.");
        }

        Assert.True(byKind.ContainsKey(".tsv"));
    }
}
