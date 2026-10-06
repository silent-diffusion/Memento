using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Status;

/// <summary>
/// The recording facts the footer shows, written by the recording coordinator and read by
/// <see cref="FooterStatusService"/>. Kept apart so neither depends on the other.
/// </summary>
public sealed class RecordingStatusBoard
{
    private RecordingFooterStatus _recording = RecordingFooterStatus.Idle;
    private string? _processingPaused;

    public RecordingFooterStatus Recording => Volatile.Read(ref _recording);

    public string? ProcessingPaused => Volatile.Read(ref _processingPaused);

    public void SetRecording(RecordingFooterStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        Volatile.Write(ref _recording, status);
    }

    public void SetProcessingPaused(string? reason) => Volatile.Write(ref _processingPaused, reason);
}
