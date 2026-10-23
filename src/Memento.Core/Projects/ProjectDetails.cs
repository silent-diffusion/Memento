using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Projects;

/// <summary>The Details sheet as stored in <c>project.json</c>.</summary>
public sealed record ProjectDetails
{
    public string Title { get; set; } = string.Empty;

    public string Type { get; set; } = "general";

    public IReadOnlyList<string> Participants { get; set; } = [];

    public string Purpose { get; set; } = string.Empty;

    public string Platform { get; set; } = string.Empty;

    public string Organization { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string Notes { get; set; } = string.Empty;

    public IReadOnlyList<string> Tags { get; set; } = [];

    public Agenda Agenda { get; set; } = Agenda.Empty;

    /// <summary>The recording's own speaker count and names (schema v3, after 1.2.0).</summary>
    public WhoSpoke WhoSpoke { get; set; } = WhoSpoke.Unknown;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
