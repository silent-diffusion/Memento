using Memento.Documents.Tests.Support;
using Xunit.Abstractions;

namespace Memento.Documents.Tests;

/// <summary>
/// Prints one line per fixture: items found against expected, uncertain count, warnings, and for images the character
/// error rate. Run with <c>--logger "console;verbosity=detailed"</c> to see it.
/// </summary>
public sealed class FixtureReportTests(ITestOutputHelper output)
{
    [Fact]
    public async Task ReportsEveryFixture()
    {
        foreach (var name in FixturePaths.All())
        {
            if (FixturePaths.IsImage(name) && !OcrSupport.IsAvailable)
            {
                output.WriteLine($"{name}: skipped ({OcrSupport.SkipReason})");
                continue;
            }

            var outcome = await FixtureRunner.RunAsync(name);
            output.WriteLine(outcome.Summary);
        }
    }
}
