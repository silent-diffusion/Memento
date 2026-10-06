using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Recording;

/// <summary>
/// Enumerates capture sources: microphones, system loopback per render endpoint, and running applications with an
/// audio session (per-process loopback). Re-enumerates on every call; ids stay stable within an app session.
/// Implemented by Memento.Audio (WASAPI) and by <see cref="Simulation.SimulatedAudioSourceProvider"/>.
/// </summary>
public interface IAudioSourceProvider
{
    Task<IReadOnlyList<AudioSource>> ListAsync(CancellationToken cancellationToken);
}
