using Memento.Core.Bridge.Contracts;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Recording.Simulation;

/// <summary>
/// An <see cref="IRecordingEngine"/> without hardware: real WAV files with generated audio, plausible levels,
/// checkpoints, pause, source changes, and on request a lost source or a full disk. Used by the tests, the soak
/// harness and <c>Memento.exe --simulate-audio</c>.
/// </summary>
public sealed class SimulatedRecordingEngine(
    SimulatedAudioSourceProvider sources,
    SimulatedEngineOptions options,
    TimeProvider time,
    ILoggerFactory loggerFactory) : IRecordingEngine
{
    private int _sessionCounter;

    public string Name => "Simulated";

    public SimulatedEngineOptions Options { get; } = options;

    /// <summary>The most recently started session (tests drive it directly).</summary>
    public SimulatedRecordingSession? LastSession { get; private set; }

    public Task<IRecordingSession> StartAsync(RecordingPlan plan, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        foreach (var source in plan.Sources)
        {
            EnsureAvailable(source);
        }

        var sessionId = "sim-" + Interlocked.Increment(ref _sessionCounter).ToString(System.Globalization.CultureInfo.InvariantCulture)
            + "-" + Guid.NewGuid().ToString("N")[..8];
        var session = new SimulatedRecordingSession(sessionId, plan, sources, Options, time, loggerFactory.CreateLogger<SimulatedRecordingSession>());
        try
        {
            session.Start();
        }
        catch
        {
            session.DisposeAsync().AsTask().GetAwaiter().GetResult();
            throw;
        }

        LastSession = session;
        return Task.FromResult<IRecordingSession>(session);
    }

    internal void EnsureAvailable(AudioSource source)
    {
        if (!sources.IsAvailable(source.Id))
        {
            throw new SourceUnavailableException(source.Id, source.Name, "the device is not connected");
        }
    }
}
