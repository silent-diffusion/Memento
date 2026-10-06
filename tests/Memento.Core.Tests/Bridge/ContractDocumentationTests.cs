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

    [Fact]
    public void EveryHostErrorCodeIsDocumentedInBridgeMd()
    {
        var errorSection = ReadRepoFile("docs", "BRIDGE.md").Split("## Error codes")[1];

        Assert.All(HostErrorCodes(), code => Assert.Contains($"`{code}`", errorSection, StringComparison.Ordinal));
    }

    [Fact]
    public void TheUiErrorCodeListIsExactlyTheHostCodes()
    {
        var ui = Literals(ReadRepoFile("ui", "src", "bridge", "types.ts"), "export const ERROR_CODES = [", "] as const");

        Assert.Equal(HostErrorCodes().Order(StringComparer.Ordinal), ui.Order(StringComparer.Ordinal));
    }

    [Fact]
    public void TheUiStageNamesAreExactlyTheHostStages()
    {
        var types = ReadRepoFile("ui", "src", "bridge", "types.ts");
        var ui = Literals(types, "export type StageName =", ";");
        var doc = Literals(ReadRepoFile("docs", "BRIDGE.md"), "type StageName =", ";");

        Assert.Equal(Constants(typeof(StageNames)), ui);
        Assert.Equal(Constants(typeof(StageNames)), doc);
    }
}
