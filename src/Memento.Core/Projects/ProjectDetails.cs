using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Projects;

/// <summary>The Details sheet as stored in <c>project.json</c>.</summary>
public sealed record ProjectDetails
{
    public string Title { get; init; } = string.Empty;

    public string Type { get; init; } = "general";

    public IReadOnlyList<string> Participants { get; init; } = [];

    public string Purpose { get; init; } = string.Empty;

    public string Platform { get; init; } = string.Empty;

    public string Organization { get; init; } = string.Empty;

    public string Location { get; init; } = string.Empty;

    public string Notes { get; init; } = string.Empty;

    public IReadOnlyList<string> Tags { get; init; } = [];

    public Agenda Agenda { get; init; } = Agenda.Empty;

    /// <summary>The recording's own speaker count and names (schema v3, after 1.2.0).</summary>
    public WhoSpoke WhoSpoke { get; init; } = WhoSpoke.Unknown;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
