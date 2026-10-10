using System.Text.Json.Serialization;

namespace Memento.Video.Tracks;

/// <summary>Source-generated JSON for the video track descriptor and its checkpoint (camelCase, like project.json).</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, WriteIndented = true)]
[JsonSerializable(typeof(VideoTrack))]
[JsonSerializable(typeof(IReadOnlyList<VideoTrack>))]
[JsonSerializable(typeof(VideoTrackCheckpoint))]
public sealed partial class VideoJsonContext : JsonSerializerContext
{
}
