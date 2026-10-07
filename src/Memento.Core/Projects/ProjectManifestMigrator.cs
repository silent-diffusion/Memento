using System.Text.Json;
using System.Text.Json.Nodes;

namespace Memento.Core.Projects;

/// <summary>
/// Brings an older <c>project.json</c> up to the current schema before it is deserialized. Each step upgrades
/// one version (<c>steps[v]</c> turns v into v + 1) and works on the raw JSON, so unknown fields survive.
/// <see cref="Default"/> has one step: v1 → v2 (<see cref="AttachmentsMigration"/>).
/// A manifest from a newer Memento is read as it is and must not be written back (see <see cref="ProjectStore"/>).
/// </summary>
public sealed class ProjectManifestMigrator
{
    private readonly IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> _steps;

    public ProjectManifestMigrator(int currentVersion, IReadOnlyDictionary<int, Func<JsonObject, JsonObject>> steps)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(currentVersion, 1);
        ArgumentNullException.ThrowIfNull(steps);
        CurrentVersion = currentVersion;
        _steps = steps;
    }

    public static ProjectManifestMigrator Default { get; } =
        new(ProjectManifest.CurrentSchemaVersion, new Dictionary<int, Func<JsonObject, JsonObject>>
        {
            [1] = AttachmentsMigration.Upgrade,
        });

    public int CurrentVersion { get; }

    /// <summary>Upgrades <paramref name="manifest"/> in steps; the result carries <see cref="CurrentVersion"/> unless it came from a newer build.</summary>
    /// <exception cref="ProjectSchemaException">The version is invalid or a step is missing.</exception>
    public ManifestMigration Migrate(JsonObject manifest)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        var from = ReadVersion(manifest);
        if (from > CurrentVersion)
        {
            return new ManifestMigration(manifest, from, Migrated: false, FromNewerVersion: true);
        }

        var current = manifest;
        for (var version = from; version < CurrentVersion; version++)
        {
            if (!_steps.TryGetValue(version, out var step))
            {
                throw new ProjectSchemaException($"There is no upgrade from project schema {version} to {version + 1}.");
            }

            current = step(current);
            current["schemaVersion"] = version + 1;
        }

        return new ManifestMigration(current, from, Migrated: from != CurrentVersion, FromNewerVersion: false);
    }

    private static int ReadVersion(JsonObject manifest)
    {
        if (!manifest.TryGetPropertyValue("schemaVersion", out var node) || node is null)
        {
            // Every manifest Memento writes has a version; a hand-made one without is read as v1.
            return 1;
        }

        if (node is JsonValue value && value.TryGetValue<int>(out var version) && version >= 1)
        {
            return version;
        }

        throw new ProjectSchemaException($"Project schema version {node.ToJsonString()} is not valid.");
    }
}
