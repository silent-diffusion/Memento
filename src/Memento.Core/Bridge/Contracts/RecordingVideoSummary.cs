using System.Text.Json.Serialization;

namespace Memento.Core.Bridge.Contracts;

/// <summary><c>RecordingSummary.video</c>: what a Library row and card show; <c>null</c> when the recording never had video.</summary>
/// <param name="Roles">The tracks the recording has (had, once removed): <c>screen</c>, <c>camera</c>.</param>
/// <param name="Height">The screen track's height, else the camera's ("1440p" on the grid card).</param>
/// <param name="RemovedAt">When Remove video ran (then <c>hasVideo</c> is false and the row reads "Video removed · audio kept"); otherwise <c>null</c>.</param>
public sealed record RecordingVideoSummary(IReadOnlyList<string> Roles, [property: JsonPropertyOrder(2)] int Height, [property: JsonPropertyOrder(3)] DateTimeOffset? RemovedAt)
{
    // Never null, whatever the library index's copy says (security audit 2, section 9).
    [JsonPropertyOrder(1)]
    public IReadOnlyList<string> Roles { get; set; } = Roles ?? [];
}
