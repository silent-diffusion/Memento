namespace Memento.Core.Engines;

/// <summary>
/// What else in Memento needs this PC's memory while a local model writes (ARCHITECTURE.md §8, "Larger models"): a
/// recording being captured, and a transcription waiting or running with the model it uses.
/// </summary>
/// <param name="Recording">A recording is being captured (or paused).</param>
/// <param name="RecordingVideo">That recording has video tracks (the encoder and its frames need more RAM).</param>
/// <param name="TranscriptionPending">A transcription or speaker pass is queued, waiting for the card, or running.</param>
/// <param name="TranscriptionVramBytes">The video memory the transcription model in effect needs on the card (0 when it runs on the processor).</param>
/// <param name="TranscriptionRamBytes">The RAM that transcription needs while it runs.</param>
public sealed record MemoryDemands(
    bool Recording,
    bool RecordingVideo,
    bool TranscriptionPending,
    long TranscriptionVramBytes,
    long TranscriptionRamBytes)
{
    /// <summary>Nothing else is running.</summary>
    public static MemoryDemands None { get; } = new(false, false, false, 0, 0);
}
