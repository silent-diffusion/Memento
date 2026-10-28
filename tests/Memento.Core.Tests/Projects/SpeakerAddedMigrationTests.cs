using System.Text.Json.Nodes;
using Memento.Core.Projects;

namespace Memento.Core.Tests.Projects;

/// <summary>Schema v7 (2.7.0): <c>details.whoSpoke.added</c>, when names were added during the recording.</summary>
public sealed class SpeakerAddedMigrationTests
{
    private static JsonObject V6(JsonNode? added = null)
    {
        var whoSpoke = new JsonObject { ["count"] = null, ["names"] = new JsonArray("Ana"), ["marks"] = new JsonArray() };
        if (added is not null)
        {
            whoSpoke["added"] = added;
        }

        return new JsonObject { ["schemaVersion"] = 6, ["details"] = new JsonObject { ["whoSpoke"] = whoSpoke, ["purpose"] = "kept" } };
    }

    [Fact]
    public void AV6ManifestGetsAnEmptyListAndKeepsTheRest()
    {
        var migrated = ProjectManifestMigrator.Default.Migrate(V6());

        Assert.True(migrated.Migrated);
        Assert.Equal(7, migrated.Manifest["schemaVersion"]!.GetValue<int>());
        var whoSpoke = migrated.Manifest["details"]!["whoSpoke"]!;
        Assert.Empty(whoSpoke["added"]!.AsArray());
        Assert.Equal("Ana", whoSpoke["names"]![0]!.GetValue<string>());
        Assert.Equal("kept", migrated.Manifest["details"]!["purpose"]!.GetValue<string>());
    }

    [Fact]
    public void AListAlreadyThereIsKeptAndAnythingElseIsReplaced()
    {
        var list = SpeakerAddedMigration.Upgrade(V6(new JsonArray(new JsonObject { ["name"] = "Ana", ["atMs"] = 5 })));
        var text = SpeakerAddedMigration.Upgrade(V6(JsonValue.Create("bad")));

        Assert.Single(list["details"]!["whoSpoke"]!["added"]!.AsArray());
        Assert.Empty(text["details"]!["whoSpoke"]!["added"]!.AsArray());
    }

    [Fact]
    public void AManifestWithoutDetailsIsLeftAlone()
    {
        var manifest = SpeakerAddedMigration.Upgrade(new JsonObject { ["schemaVersion"] = 6 });

        Assert.Null(manifest["details"]);
    }
}
