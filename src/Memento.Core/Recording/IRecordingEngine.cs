namespace Memento.Core.Recording;

/// <summary>
/// Starts capture sessions. One session at a time is enough (the coordinator refuses a second).
/// Implemented by Memento.Audio (WASAPI) and by <see cref="Simulation.SimulatedRecordingEngine"/>.
/// </summary>
public interface IRecordingEngine
{
    /// <summary>A short name for logs and the History tab, e.g. <c>WASAPI</c> or <c>Simulated</c>.</summary>
    string Name { get; }

    /// <summary>
    /// How often writers hand their buffered audio to Windows between checkpoints, or <c>null</c> if only checkpoints
    /// do. Written to <c>recording.state.json</c> for recovery's "may be missing" estimate.
    /// </summary>
    TimeSpan? FlushInterval => null;

    /// <summary>
    /// Opens every source in <paramref name="plan"/> and starts writing one WAV per source under
    /// <c>&lt;project&gt;/tracks/</c>. Either all sources start or none do.
    /// </summary>
    /// <exception cref="SourceUnavailableException">A source could not be opened; nothing was started.</exception>
    Task<IRecordingSession> StartAsync(RecordingPlan plan, CancellationToken cancellationToken);
}
