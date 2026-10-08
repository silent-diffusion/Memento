using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Engines;
using Memento.Core.Host;
using Memento.Core.Library;
using Memento.Core.Settings;

namespace Memento.Core.Status;

/// <summary>
/// Computes the status footer (engine readiness, free space on the library drive, recording checkpoint and lost
/// source, why processing is paused) and publishes <c>status.footer</c> when it changes.
/// </summary>
public sealed class FooterStatusService(
    ISettingsStore settings,
    ILibraryLocation library,
    IFreeSpaceProbe freeSpace,
    RecordingStatusBoard board,
    BridgeEventPublisher publisher,
    EngineStatusService engines,
    ProcessingGate gate,
    Export.ExportStatusBoard? exports = null,
    Updates.UpdateStatusBoard? updates = null)
{
    /// <summary>Default low-space threshold (ARCHITECTURE.md §5.7): 10 GB. Settings › Recording can change it.</summary>
    public const long LowSpaceThresholdBytes = 10L * 1024 * 1024 * 1024;

    /// <summary>The words <see cref="FooterStatusPayload.ProcessingPaused"/> carries when space runs low.</summary>
    public const string LowSpaceReason = "Low disk space";

    private readonly object _gate = new();
    private string? _lastPublished;

    /// <summary>
    /// The footer now. <c>engine.ready</c> means the transcription model in effect is installed; <c>engine.detail</c>
    /// says where it would run. <c>processingPaused</c> is the reason heavy stages wait: low disk space, "PC is busy"
    /// (recording, or the processor busy) or "Paused by you".
    /// </summary>
    public FooterStatusPayload Compute()
    {
        var free = freeSpace.GetFreeBytes(library.Root);
        var threshold = settings.Current.Recording.LowSpaceThresholdBytes;
        var low = free is not null && free < threshold;
        var storage = new StorageStatus(free, low);
        var paused = board.ProcessingPaused ?? (low ? LowSpaceReason : gate.Reason);
        // The card's memory and the shortfall sentence are for Settings; left out here so the footer, sampled every few
        // seconds, is not re-sent each time another program's memory use moves.
        var detail = engines.Compute().Transcription with { GpuMemory = null, Note = null };
        return new FooterStatusPayload(new EngineStatus(detail.Ready, detail.Device, detail), storage, board.Recording, paused)
        {
            Export = exports?.Current ?? ExportFooterStatus.Idle,
            Update = updates?.Current ?? UpdateFooterStatus.Idle,
        };
    }

    /// <summary>Publishes the current status if it differs from the last one sent, or always when <paramref name="force"/> is set.</summary>
    /// <returns><c>true</c> if an event was posted.</returns>
    public bool Publish(bool force)
    {
        var payload = Compute();
        var json = BridgeEventPublisher.SerializeFooterStatus(payload);
        lock (_gate)
        {
            if (!force && json == _lastPublished)
            {
                return false;
            }

            _lastPublished = json;
        }

        publisher.PublishFooterStatus(payload);
        return true;
    }
}
