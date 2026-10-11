namespace Memento.Core.Recording;

/// <summary>Input to <see cref="ITrackFinalizer.FinalizeAsync"/>.</summary>
/// <param name="ProjectFolder">Full path of the project folder.</param>
/// <param name="Tracks">Capture files to finalize.</param>
/// <param name="Storage">Codec and options from Settings › Recording.</param>
public sealed record FinalizeRequest(string ProjectFolder, IReadOnlyList<FinalizeTrackInput> Tracks, StorageFormat Storage)
{
    /// <summary>"Even out source levels" (Settings › Recording › Storage): lift a quiet track to the loudest one's speech level in the mix.</summary>
    public bool EvenLevels { get; init; }
}
