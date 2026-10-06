namespace Memento.Core.Recording;

/// <summary>What <see cref="ITrackFinalizer"/> produced. Paths are relative to the project folder, forward slashes.</summary>
/// <param name="Codec">The codec actually used; differs from the requested one when its encoder is not installed.</param>
/// <param name="Notes">Plain-language remarks for the History tab, e.g. a codec fallback.</param>
/// <param name="ObsoleteCaptureFiles">
/// Capture WAVs replaced by encoded files. The caller deletes them only after the manifest that points at the
/// encoded files is saved, so an interruption at any point leaves either the WAVs or a complete manifest.
/// </param>
public sealed record FinalizedAudio(
    IReadOnlyList<FinalizedTrack> Tracks,
    FinalizedFile Mix,
    string PeaksFile,
    string Codec,
    DateTimeOffset ComputedAt,
    IReadOnlyList<string> Notes,
    IReadOnlyList<string> ObsoleteCaptureFiles);
