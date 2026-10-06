using System.Text.Json;
using System.Text.Json.Serialization;

namespace Memento.Core.Projects;

/// <summary>Source-generated serialization for every file in a project folder.</summary>
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
[JsonSerializable(typeof(ProjectManifest))]
[JsonSerializable(typeof(AnnotationsDocument))]
[JsonSerializable(typeof(HistoryLine))]
[JsonSerializable(typeof(RecordingStateDocument))]
internal sealed partial class ProjectJsonContext : JsonSerializerContext
{
    /// <summary>The same contracts written on one line, for <c>history.jsonl</c>.</summary>
    /// <remarks>Lazy: static initializers in partial files run in no fixed order relative to <c>Default</c>.</remarks>
    public static ProjectJsonContext Compact => CompactHolder.Instance;

    private static class CompactHolder
    {
        public static readonly ProjectJsonContext Instance = new(new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true,
        });
    }
}
