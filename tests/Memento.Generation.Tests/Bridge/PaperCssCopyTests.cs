using Memento.Documents.Render;

namespace Memento.Generation.Tests.Bridge;

/// <summary>
/// The paper markup the host sends (<c>documents.renderHtml</c>, <c>generation.previewHtml</c>, <c>styles.sampleHtml</c>) is
/// the <c>article.paper</c> element only; the UI styles it with its own copy of <see cref="PaperCss.Stylesheet"/>
/// (ui/src/components/paper/paper.css), because its CSP forbids inline styles. The two must stay identical.
/// </summary>
public sealed class PaperCssCopyTests
{
    [Fact]
    public void TheUisPaperStylesheetIsTheHostsVerbatim()
    {
        var copy = File.ReadAllText(Path.Combine(RepoRoot(), "ui", "src", "components", "paper", "paper.css")).Replace("\r\n", "\n", StringComparison.Ordinal);

        // The file starts with a comment saying where it comes from; the rest is the stylesheet.
        var start = copy.IndexOf("*/", StringComparison.Ordinal);
        var body = (copy.StartsWith("/*", StringComparison.Ordinal) && start >= 0 ? copy[(start + 2)..] : copy).Trim();

        Assert.Equal(PaperCss.Stylesheet.Replace("\r\n", "\n", StringComparison.Ordinal).Trim(), body);
    }

    private static string RepoRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Memento.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Memento.sln was not found above the test output folder.");
    }
}
