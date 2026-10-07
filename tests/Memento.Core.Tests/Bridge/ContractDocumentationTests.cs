using System.Reflection;
using System.Text.RegularExpressions;
using Memento.Core.Bridge;
using Memento.Core.Projects;

namespace Memento.Core.Tests.Bridge;

/// <summary>
/// Keeps the host's error codes and stage names in step with docs/BRIDGE.md and ui/src/bridge/types.ts, which are
/// written by hand on both sides of the bridge.
/// </summary>
public sealed partial class ContractDocumentationTests
{
    private static readonly Lazy<string> RepoRoot = new(FindRepoRoot);

    private static string FindRepoRoot()
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

    private static string ReadRepoFile(params string[] parts) => File.ReadAllText(Path.Combine([RepoRoot.Value, .. parts]));

    private static List<string> Constants(Type type) =>
        type.GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToList();

    private static List<string> HostErrorCodes() => [.. Constants(typeof(BridgeErrorCodes)), .. Constants(typeof(DomainErrorCodes))];

    /// <summary>The string literals between <paramref name="start"/> and the next <paramref name="end"/>.</summary>
    private static List<string> Literals(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"'{start}' not found");
        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        return QuotedLiteral().Matches(source[(from + start.Length)..to]).Select(m => m.Groups[1].Value).ToList();
    }

    [GeneratedRegex("'([^']*)'")]
    private static partial Regex QuotedLiteral();

    [Fact]
    public void RouterCodesUseTheBridgePrefix()
    {
        Assert.All(Constants(typeof(BridgeErrorCodes)), code => Assert.StartsWith("bridge.", code, StringComparison.Ordinal));
        Assert.All(Constants(typeof(DomainErrorCodes)), code => Assert.DoesNotContain("bridge.", code, StringComparison.Ordinal));
    }


    /// <summary>What the "Clarifications (M2)" section of BRIDGE.md added after the UI landed.</summary>
    private static readonly string[] ClarificationStages = [StageNames.Topics];
    private static readonly string[] ClarificationCodes = [DomainErrorCodes.ModelsBusy];

    private static string Doc => ReadRepoFile("docs", "BRIDGE.md");

    private static bool DocHasClarifications => Doc.Contains("## Clarifications (M2", StringComparison.Ordinal);

    /// <summary>From each <paramref name="heading"/> to the next <c>## </c> heading.</summary>
    private static string Sections(string heading)
    {
        var parts = Doc.Split(heading).Skip(1).Select(p => p.Split("\n## ")[0]);
        return string.Join('\n', parts);
    }

    /// <summary>The codes in backticks in BRIDGE.md's "## Error codes (M2)" section.</summary>
    private static List<string> M2DocumentedCodes() =>
        BacktickedCode().Matches(Sections("## Error codes (M2)")).Select(m => m.Groups[1].Value).ToList();

    [GeneratedRegex("`([a-z]+\\.[a-zA-Z.]+)`")]
    private static partial Regex BacktickedCode();

    [Fact]
    public void EveryHostErrorCodeIsDocumentedInBridgeMd()
    {
        // The M0/M1 "## Error codes" section, the M2 one, and the M2 clarifications.
        var documented = Sections("## Error codes") + Sections("## Clarifications (M2");
        var expected = DocHasClarifications ? HostErrorCodes() : HostErrorCodes().Except(ClarificationCodes, StringComparer.Ordinal).ToList();

        Assert.All(expected, code => Assert.Contains($"`{code}`", documented, StringComparison.Ordinal));
    }

    [Fact]
    public void TheUiErrorCodeListIsExactlyTheHostCodes()
    {
        var ui = Literals(ReadRepoFile("ui", "src", "bridge", "types.ts"), "export const ERROR_CODES = [", "] as const");
        var expected = HostErrorCodes();

        // The host and the UI land M2 in separate changes: until the UI lists the M2 codes, it must list exactly the
        // others. Once it lists any of them, it must list them all; additions from the clarifications follow at the
        // integration pass.
        var m2 = M2DocumentedCodes();
        if (!ui.Intersect(m2, StringComparer.Ordinal).Any())
        {
            expected = expected.Except(m2, StringComparer.Ordinal).ToList();
        }

        expected = expected.Except(ClarificationCodes.Except(ui, StringComparer.Ordinal), StringComparer.Ordinal).ToList();
        Assert.Equal(expected.Order(StringComparer.Ordinal), ui.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryM2CodeInBridgeMdIsAHostCode()
    {
        Assert.Equal(M2DocumentedCodes().Order(StringComparer.Ordinal), HostErrorCodes().Intersect(M2DocumentedCodes(), StringComparer.Ordinal).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheUiStageNamesAreExactlyTheHostStages()
    {
        var types = ReadRepoFile("ui", "src", "bridge", "types.ts");
        var ui = Literals(types, "export type StageName =", ";");
        var doc = Literals(Doc, "type StageName =", ";");
        var host = Constants(typeof(StageNames));

        // `topics` came with the M2 clarifications; the type lines catch up at the integration pass.
        Assert.Equal(host.Except(ClarificationStages.Except(ui, StringComparer.Ordinal), StringComparer.Ordinal), ui);
        Assert.Equal(host.Except(ClarificationStages.Except(doc, StringComparer.Ordinal), StringComparer.Ordinal), doc);
    }
}
