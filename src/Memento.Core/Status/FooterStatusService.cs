using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Host;
using Memento.Core.Settings;

namespace Memento.Core.Status;

/// <summary>
/// Computes the Library footer status (engine readiness and free space on the library drive)
/// and publishes <c>status.footer</c> when it changes.
/// </summary>
public sealed class FooterStatusService(
    ISettingsStore settings,
    IFreeSpaceProbe freeSpace,
    BridgeEventPublisher publisher)
{
    /// <summary>Default low-space threshold (ARCHITECTURE.md §5.7): 10 GB.</summary>
    public const long LowSpaceThresholdBytes = 10L * 1024 * 1024 * 1024;

    private readonly object _gate = new();
    private FooterStatusPayload? _lastPublished;

    /// <summary>
    /// The engine is never ready in this version: no transcription engine ships before M2.
    /// The footer says so plainly instead of showing a made-up state.
    /// </summary>
    public FooterStatusPayload Compute()
    {
        var free = freeSpace.GetFreeBytes(settings.Current.EffectiveLibraryPath);
        var storage = new StorageStatus(free, free is not null && free < LowSpaceThresholdBytes);
        return new FooterStatusPayload(new EngineStatus(Ready: false, Device: null), storage);
    }

    /// <summary>Publishes the current status if it differs from the last one sent, or always when <paramref name="force"/> is set.</summary>
    /// <returns><c>true</c> if an event was posted.</returns>
    public bool Publish(bool force)
    {
        var current = Compute();
        lock (_gate)
        {
            if (!force && current == _lastPublished)
            {
                return false;
            }

            _lastPublished = current;
        }

        publisher.PublishFooterStatus(current);
        return true;
    }
}
