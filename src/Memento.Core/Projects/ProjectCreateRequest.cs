namespace Memento.Core.Projects;

/// <summary>What <see cref="IProjectStore.CreateAsync"/> needs to start a project.</summary>
/// <param name="CreatedAt">Local time with offset; the id is derived from it.</param>
public sealed record ProjectCreateRequest(string Title, string Type, DateTimeOffset CreatedAt, string State);
