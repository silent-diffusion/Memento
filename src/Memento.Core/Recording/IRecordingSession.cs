using Memento.Core.Bridge.Contracts;

namespace Memento.Core.Recording;

/// <summary>
/// One running capture. Rules for implementations (ARCHITECTURE.md §5, §9):
/// <list type="bullet">
/// <item>Capture threads never wait on consumers: every event is raised through a <see cref="SessionEventDispatcher"/>
/// (or an equivalent queue), never on a capture or writer thread.</item>
/// <item>Each track streams PCM through a <see cref="Audio.StreamingWavWriter"/> fed from a bounded buffer by its own writer.</item>
/// <item>Every <see cref="RecordingPlan.CheckpointInterval"/> all writers flush and patch their headers, then
/// <see cref="CheckpointWritten"/> is raised.</item>
/// <item>Pause stops writing but keeps capture open; resume continues the same files.</item>
/// <item>A write failing for lack of space stops the session cleanly (headers patched) and raises <see cref="HostStopped"/>
/// with <see cref="HostStopReason.DiskFull"/>.</item>
/// </list>
/// </summary>
public interface IRecordingSession : IAsyncDisposable
{
    string SessionId { get; }

    DateTimeOffset StartedAt { get; }

    RecordingSessionState State { get; }

    /// <summary>Recorded time on the session timeline, excluding pauses.</summary>
    long ElapsedMs { get; }

    /// <summary>A snapshot of every track started in this session, ended ones included.</summary>
    IReadOnlyList<SessionTrack> Tracks { get; }

    IReadOnlyList<SessionPause> Pauses { get; }

    DateTimeOffset? LastCheckpointAt { get; }

    Task PauseAsync(CancellationToken cancellationToken);

    Task ResumeAsync(CancellationToken cancellationToken);

    /// <summary>Starts a new track for <paramref name="source"/> or ends the open track of that source.</summary>
    /// <exception cref="SourceUnavailableException">Enabling a source that cannot be opened; the other tracks are untouched.</exception>
    Task<IReadOnlyList<SessionTrack>> SetSourceAsync(AudioSource source, bool enabled, CancellationToken cancellationToken);

    /// <summary>Returns the current <see cref="ElapsedMs"/> for a highlight.</summary>
    Task<long> MarkAsync(CancellationToken cancellationToken);

    /// <summary>Flushes every writer and patches headers now (crash handler, before a risky operation).</summary>
    Task CheckpointAsync(CancellationToken cancellationToken);

    /// <summary>Stops capture, closes every writer and returns the final tracks. Idempotent; after a host stop it returns that result.</summary>
    Task<RecordingSessionResult> StopAsync(CancellationToken cancellationToken);

    /// <summary>Meter readings, at most 30 per second.</summary>
    event EventHandler<LevelsEventArgs>? LevelsAvailable;

    event EventHandler<SessionStateChangedEventArgs>? StateChanged;

    event EventHandler<CheckpointEventArgs>? CheckpointWritten;

    /// <summary>A device went away: that track ended, the others continue.</summary>
    event EventHandler<SourceLostEventArgs>? SourceLost;

    /// <summary>The session stopped on its own (disk full, every device lost, fatal error). Tracks are closed.</summary>
    event EventHandler<HostStoppedEventArgs>? HostStopped;
}
