using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Projects;

/// <summary><c>annotations.json</c>, schema v1: chapters, highlights, topics (notes arrive later and are preserved).</summary>
public sealed record AnnotationsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<Chapter> Chapters { get; init; } = [];

    public IReadOnlyList<Highlight> Highlights { get; init; } = [];

    public IReadOnlyList<Topic> Topics { get; init; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
