using System.Text.Json.Serialization;
using Memento.Core.Tests.Serialization;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Styling;
using Memento.Documents.Templates;
using Memento.Generation.Ai;
using Memento.Generation.Documents;
using Xunit.Abstractions;

namespace Memento.Generation.Tests.Units;

/// <summary>
/// Security audit 2, §9, for the files Memento.Documents and Memento.Generation read: documents and their versions,
/// templates, styles and the local model's load history. A list or object member the JSON leaves out must keep its
/// default (an initializer and a setter, or <c>?? []</c> on a positional record), or be <c>required</c>.
/// </summary>
public sealed class NonNullableDocumentMembersTests(ITestOutputHelper output)
{
    public static TheoryData<string> Files => [.. FilesByName.Keys];

    private static Dictionary<string, JsonSerializerContext> FilesByName { get; } = new(StringComparer.Ordinal)
    {
        ["documents/<id>.json"] = DocumentJsonContext.Default,
        ["templates/<id>.json"] = TemplateJsonContext.Default,
        ["styles/<id>.json"] = StyleJsonContext.Default,
        ["document versions"] = GenerationJsonContext.Default,
        ["local model load history"] = LocalLoadHistoryJsonContext.Default,
    };

    [Theory]
    [MemberData(nameof(Files))]
    public void NoListOrObjectOfAFileReadsAsNullWhenTheJsonLeavesItOut(string file)
    {
        var types = NonNullableContracts.ObjectTypes(FilesByName[file]);
        output.WriteLine($"{file}: {types.Count} types, {NonNullableContracts.GuardedMemberCount(types)} list and object members");
        var problems = NonNullableContracts.FindNullsReadFromEmptyObjects(FilesByName[file]);

        Assert.True(problems.Count == 0, string.Join(Environment.NewLine, problems));
    }

    [Fact]
    public void TheWalkSeesTheDocumentModel()
    {
        var types = NonNullableContracts.ObjectTypes(DocumentJsonContext.Default);

        Assert.Contains(typeof(Document), types);
        Assert.Contains(typeof(TableBlock), types);
    }
}
