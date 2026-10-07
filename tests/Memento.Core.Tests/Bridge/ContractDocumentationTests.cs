using System.Reflection;
using System.Text.RegularExpressions;
using Memento.Core.Bridge;
using Memento.Core.Projects;

namespace Memento.Core.Tests.Bridge;

/// <summary>
/// Keeps the host's error codes and stage names in step with docs/BRIDGE.md and ui/src/bridge/types.ts, which are
/// written by hand on both sides of the bridge. Since the M2, M3 and M4 integrations both sides list everything, so
/// the checks are exact.
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

    private static string Doc => ReadRepoFile("docs", "BRIDGE.md");

    /// <summary>From each <paramref name="heading"/> to the next <c>## </c> heading.</summary>
    private static string Sections(string heading)
    {
        var parts = Doc.Split(heading).Skip(1).Select(p => p.Split("\n## ")[0]);
        return string.Join('\n', parts);
    }

    /// <summary>The codes in backticks in BRIDGE.md's "## Error codes (M2)" section.</summary>
    private static List<string> M2DocumentedCodes() =>
        BacktickedCode().Matches(Sections("## Error codes (M2)")).Select(m => m.Groups[1].Value).ToList();

    /// <summary>The codes in backticks in BRIDGE.md's "## Error codes (M3)" section.</summary>
    private static List<string> M3DocumentedCodes() =>
        BacktickedCode().Matches(Sections("## Error codes (M3)")).Select(m => m.Groups[1].Value).ToList();

    [GeneratedRegex("`([a-z]+\\.[a-zA-Z.]+)`")]
    private static partial Regex BacktickedCode();

    [Fact]
    public void RouterCodesUseTheBridgePrefix()
    {
        Assert.All(Constants(typeof(BridgeErrorCodes)), code => Assert.StartsWith("bridge.", code, StringComparison.Ordinal));
        Assert.All(Constants(typeof(DomainErrorCodes)), code => Assert.DoesNotContain("bridge.", code, StringComparison.Ordinal));
    }

    [Fact]
    public void EveryHostErrorCodeIsListedInAnErrorCodesSection()
    {
        // The M0/M1 "## Error codes" section, "## Error codes (M2)" and "## Error codes (M3)".
        var documented = Sections("## Error codes");

        Assert.All(HostErrorCodes(), code => Assert.Contains($"`{code}`", documented, StringComparison.Ordinal));
    }

    [Fact]
    public void TheUiErrorCodeListIsExactlyTheHostCodes()
    {
        var ui = Literals(ReadRepoFile("ui", "src", "bridge", "types.ts"), "export const ERROR_CODES = [", "] as const");

        Assert.Equal(HostErrorCodes().Order(StringComparer.Ordinal), ui.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void EveryM4HostCodeIsInTheM4ErrorCodesSection()
    {
        // The M4 families, less the codes M3 introduced (ai.keyWriteFailed came with the key store).
        string[] prefixes = ["ai.", "generation.", "templates.", "styles.", "documents."];
        var m4 = HostErrorCodes().Where(c => prefixes.Any(p => c.StartsWith(p, StringComparison.Ordinal))).Except(M3DocumentedCodes(), StringComparer.Ordinal).ToList();

        Assert.NotEmpty(m4);
        Assert.Empty(m4.Except(M4DocumentedCodes(), StringComparer.Ordinal));
    }

    /// <summary>The codes in backticks in BRIDGE.md's "## Error codes (M4)" section.</summary>
    private static List<string> M4DocumentedCodes() =>
        BacktickedCode().Matches(Sections("## Error codes (M4)")).Select(m => m.Groups[1].Value).ToList();

    [Fact]
    public void EveryM4CodeInBridgeMdIsAHostCode()
    {
        var documented = M4DocumentedCodes();

        Assert.NotEmpty(documented);
        Assert.Empty(documented.Except(HostErrorCodes(), StringComparer.Ordinal));
    }

    [Fact]
    public void EveryM2CodeInBridgeMdIsAHostCode()
    {
        var documented = M2DocumentedCodes();

        Assert.NotEmpty(documented);
        Assert.Empty(documented.Except(HostErrorCodes(), StringComparer.Ordinal));
    }

    [Fact]
    public void EveryM3CodeInBridgeMdIsAHostCode()
    {
        var documented = M3DocumentedCodes();

        Assert.NotEmpty(documented);
        Assert.Empty(documented.Except(HostErrorCodes(), StringComparer.Ordinal));
    }

    [Fact]
    public void TheUiAndDocumentedStageNamesAreExactlyTheHostStagesInPipelineOrder()
    {
        var ui = Literals(ReadRepoFile("ui", "src", "bridge", "types.ts"), "export type StageName =", ";");
        var doc = Literals(Doc, "type StageName =", ";");
        var host = Constants(typeof(StageNames));

        Assert.Equal(host, ui);
        Assert.Equal(host, doc);
    }
}
