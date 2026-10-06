using System.Text.Json.Nodes;

namespace Memento.Core.Projects;

/// <summary>Result of <see cref="ProjectManifestMigrator.Migrate"/>.</summary>
/// <param name="FromVersion">The version the file was written with.</param>
/// <param name="FromNewerVersion">Written by a newer Memento: readable, but this build must not write it back.</param>
public sealed record ManifestMigration(JsonObject Manifest, int FromVersion, bool Migrated, bool FromNewerVersion);
