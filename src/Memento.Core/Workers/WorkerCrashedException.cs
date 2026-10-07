using System.Globalization;

namespace Memento.Core.Workers;

/// <summary>The worker process ended without a result (a native abort, out of memory, killed).</summary>
public sealed class WorkerCrashedException : Exception
{
    public WorkerCrashedException()
    {
    }

    public WorkerCrashedException(string message)
        : base(message)
    {
    }

    public WorkerCrashedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public WorkerCrashedException(int exitCode, IReadOnlyList<string> errorTail)
        : base(string.Create(CultureInfo.InvariantCulture, $"The worker exited with code 0x{exitCode:X8} without a result."))
    {
        ExitCode = exitCode;
        ErrorTail = errorTail;
    }

    /// <summary>A worker that sent nothing for <paramref name="quietFor"/> and was stopped (hung, not crashed).</summary>
    public WorkerCrashedException(TimeSpan quietFor, int exitCode, IReadOnlyList<string> errorTail)
        : base(string.Create(CultureInfo.InvariantCulture, $"The worker sent nothing for {Minutes(quietFor)} and was stopped as hung."))
    {
        QuietFor = quietFor;
        ExitCode = exitCode;
        ErrorTail = errorTail;
    }

    public int ExitCode { get; }

    /// <summary>Set when the worker was stopped because it went quiet for this long, rather than ending by itself.</summary>
    public TimeSpan? QuietFor { get; }

    public bool Hung => QuietFor is not null;

    public IReadOnlyList<string> ErrorTail { get; } = [];

    /// <summary>The exit code or stderr says the engine ran out of memory.</summary>
    public bool LooksLikeOutOfMemory =>
        ExitCode == unchecked((int)0xC0000017) // STATUS_NO_MEMORY
        || ErrorTail.Any(l => l.Contains("out of memory", StringComparison.OrdinalIgnoreCase) || l.Contains("ErrorOutOfDeviceMemory", StringComparison.OrdinalIgnoreCase) || l.Contains("failed to allocate", StringComparison.OrdinalIgnoreCase));

    /// <summary>The exit code is a fast-fail native abort (0xC0000409), what whisper.cpp does when a backend fails.</summary>
    public bool LooksLikeNativeAbort => !Hung && ExitCode == unchecked((int)0xC0000409);

    private static string Minutes(TimeSpan span) =>
        span.TotalMinutes >= 2
            ? string.Create(CultureInfo.InvariantCulture, $"{Math.Round(span.TotalMinutes)} minutes")
            : string.Create(CultureInfo.InvariantCulture, $"{Math.Max(0, span.TotalSeconds):0.###} seconds");
}
