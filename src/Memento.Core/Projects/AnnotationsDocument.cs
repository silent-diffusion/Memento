using System.Text.Json;
using System.Text.Json.Serialization;
using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Projects;

/// <summary><c>annotations.json</c>, schema v1: chapters, highlights, topics (notes arrive later and are preserved).</summary>
public sealed record AnnotationsDocument
{
    public const int CurrentSchemaVersion = 1;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    public IReadOnlyList<Chapter> Chapters { get; set; } = [];

    public IReadOnlyList<Highlight> Highlights { get; set; } = [];

    public IReadOnlyList<Topic> Topics { get; set; } = [];

    /// <summary>
    /// 2.0: the times (ms) of chapter suggestions the user dismissed, so they are not suggested again. Optional and
    /// additive (still schema v1): an older Memento keeps it as an unknown field. A setter, not init: source-generated
    /// JSON sets a missing init-only member to null, and every annotations.json written by 1.x lacks this one.
    /// </summary>
    public IReadOnlyList<long> DismissedSuggestions { get; set; } = [];

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }
}
