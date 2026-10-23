using System.Text.Json.Nodes;
using Memento.Core.Serialization;
using Memento.Documents.Model;
using Memento.Documents.Model.Blocks;
using Memento.Documents.Tests.DocumentModel.Support;

namespace Memento.Documents.Tests.DocumentModel;

/// <summary>
/// Security audit 2, §9, for documents/&lt;id&gt;.json: a list or object written as <c>null</c> or left out reads as its
/// default, a null block is dropped, and a left-out value keeps its initializer (before 2.4.0 a module without a
/// provenance kind read as written by the AI).
/// </summary>
public sealed class DocumentNullMembersTests
{
    [Fact]
    public void NullListsAndObjectsReadAsTheirDefaultsAndNullBlocksAreDropped()
    {
        var node = JsonNode.Parse(DocumentJson.Serialize(SampleDocuments.AllShapes()))!.AsObject();
        var module = node["rows"]![0]!["modules"]![0]!.AsObject();
        var blocks = module["blocks"]!.AsArray();
        var count = blocks.Count;
        blocks[1]!["runs"] = null;
        blocks.Add(null);
        module["provenance"] = null;
        node["meta"] = null;

        var document = DocumentJson.Deserialize(node.ToJsonString());

        var read = document.Rows[0].Modules[0];
        Assert.Equal(count, read.Blocks.Count);
        Assert.Empty(Assert.IsType<ParagraphBlock>(read.Blocks[1]).Runs);
        Assert.Equal(ProvenanceKind.User, read.Provenance.Kind);
        Assert.NotNull(document.Meta);
        Assert.Null(NullMembers.FindFirst(document));
    }

    [Fact]
    public void ALeftOutProvenanceKindIsTheUsersNotTheAis()
    {
        var node = JsonNode.Parse(DocumentJson.Serialize(SampleDocuments.AllShapes()))!.AsObject();
        node["rows"]![0]!["modules"]![0]!["provenance"]!.AsObject().Remove("kind");

        var document = DocumentJson.Deserialize(node.ToJsonString());

        Assert.Equal(ProvenanceKind.User, document.Rows[0].Modules[0].Provenance.Kind);
    }

    [Fact]
    public void ADocumentWithoutRowsIsAnEmptyDocumentNotANull()
    {
        var document = DocumentJson.Deserialize("""{ "schemaVersion": 1, "id": "d1", "title": "Minutes", "rows": null }""");

        Assert.Empty(document.Rows);
        Assert.Equal(1, document.Version);
    }
}
