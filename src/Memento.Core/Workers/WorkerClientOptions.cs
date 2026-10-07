namespace Memento.Core.Workers;

/// <summary>Limits for <see cref="WorkerClient"/>; the defaults are the production values, tests shorten them.</summary>
public sealed record WorkerClientOptions
{
    public static WorkerClientOptions Default { get; } = new();

    /// <summary>
    /// How long a worker may send no protocol line before it counts as hung and is stopped. Workers report progress
    /// every few seconds while they transcribe, diarize or generate; the longest legitimate silence is a local model
    /// reading a long prompt on a slow processor (minutes), so the limit sits well above that. A worker waiting for the
    /// machine-wide graphics-card lock says so every few minutes.
    /// </summary>
    public TimeSpan QuietLimit { get; init; } = TimeSpan.FromMinutes(30);

    /// <summary>How long a worker that sent its final line may take to exit before it is killed.</summary>
    public TimeSpan ExitGrace { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>How long a killed worker is waited for; after that it is logged and left to Windows (its job object).</summary>
    public TimeSpan KillGrace { get; init; } = TimeSpan.FromSeconds(10);
}
