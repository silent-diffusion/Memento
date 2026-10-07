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

    public int ExitCode { get; }

    public IReadOnlyList<string> ErrorTail { get; } = [];

    /// <summary>The exit code or stderr says the engine ran out of memory.</summary>
    public bool LooksLikeOutOfMemory =>
        ExitCode == unchecked((int)0xC0000017) // STATUS_NO_MEMORY
        || ErrorTail.Any(l => l.Contains("out of memory", StringComparison.OrdinalIgnoreCase) || l.Contains("ErrorOutOfDeviceMemory", StringComparison.OrdinalIgnoreCase) || l.Contains("failed to allocate", StringComparison.OrdinalIgnoreCase));

    /// <summary>The exit code is a fast-fail native abort (0xC0000409), what whisper.cpp does when a backend fails.</summary>
    public bool LooksLikeNativeAbort => ExitCode == unchecked((int)0xC0000409);
}
