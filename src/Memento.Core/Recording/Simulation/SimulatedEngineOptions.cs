namespace Memento.Core.Recording.Simulation;

/// <summary>How the simulated engine behaves.</summary>
public sealed record SimulatedEngineOptions
{
    /// <summary>
    /// Real-time multiplier for the generator thread (1 = real time). <c>0</c> means manual: no thread runs and
    /// audio is produced only by <see cref="SimulatedRecordingSession.Advance"/>, which makes tests deterministic.
    /// </summary>
    public double Speed { get; init; } = 1.0;

    /// <summary>Capture packet length; WASAPI's default period is 10 ms.</summary>
    public int BlockMs { get; init; } = 10;

    /// <summary>Per-track ring buffer between capture and writer, in packets (500 × 10 ms = 5 s).</summary>
    public int BufferBlocks { get; init; } = 500;

    /// <summary>Simulated time after which <see cref="LoseSourceId"/> is unplugged.</summary>
    public TimeSpan? LoseSourceAfter { get; init; }

    /// <summary>The source to lose; the system audio source when <c>null</c>.</summary>
    public string? LoseSourceId { get; init; }

    /// <summary>Simulated time after which every write fails with "disk full".</summary>
    public TimeSpan? DiskFullAfter { get; init; }

    /// <summary>Noise seed, so runs are reproducible.</summary>
    public int Seed { get; init; } = 1;
}
