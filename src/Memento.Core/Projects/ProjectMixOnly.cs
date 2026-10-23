using System.Text.Json.Serialization;

namespace Memento.Core.Projects;

/// <summary>
/// Set in <c>project.json</c> once "Keep only the mix" removed the recording's separate track files (2.0). The tracks
/// stay listed (their names, offsets and lengths, which the transcript's lines and the speakers refer to) without a
/// hash, and only the mix is kept: speakers cannot be identified per track again and tracks cannot be exported.
/// </summary>
/// <param name="At">When the track files were removed.</param>
/// <param name="TrackIds">The tracks whose files were removed.</param>
/// <param name="BytesFreed">The size of the removed files.</param>
public sealed record ProjectMixOnly(DateTimeOffset At, IReadOnlyList<string> TrackIds, [property: JsonPropertyOrder(2)] long BytesFreed)
{
    // Never null, whatever the JSON says: a missing or null list reads as empty (security audit 2, section 9).
    [JsonPropertyOrder(1)]
    public IReadOnlyList<string> TrackIds { get; set; } = TrackIds ?? [];
}
