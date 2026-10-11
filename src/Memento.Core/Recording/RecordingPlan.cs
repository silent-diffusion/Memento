using Memento.Core.Bridge.Contracts;
using Memento.Core.Audio;

namespace Memento.Core.Recording;

/// <summary>What to record and where.</summary>
/// <param name="ProjectFolder">Full path of the project folder; tracks go to its <c>tracks</c> subfolder.</param>
/// <param name="Sources">Sources to start, in the order the user listed them.</param>
/// <param name="CheckpointInterval">Settings › Recording › checkpoint (30 s by default).</param>
/// <param name="PreferredFormat">
/// Capture format to ask for, or <c>null</c> to keep each device's native mix format (the default: resampling
/// happens only at mixdown).
/// </param>
public sealed record RecordingPlan(
    string ProjectFolder,
    IReadOnlyList<AudioSource> Sources,
    TimeSpan CheckpointInterval,
    PcmFormat? PreferredFormat = null)
{
    /// <summary>
    /// 2.1, "Resume recording": track ids (and so capture file stems) the project already uses. Engines never give a new
    /// track one of these, so a resumed segment's files never touch the finalized tracks of earlier segments.
    /// </summary>
    public IReadOnlyCollection<string> ReservedTrackIds { get; init; } = [];
}
