using System.Runtime.InteropServices;

namespace Memento.Worker;

/// <summary>
/// Turns off the Windows dialogs a crash or a missing drive would show: a worker has no window and nobody to answer
/// one, so a native fault must end the process (the host then reports a crash) instead of leaving it waiting.
/// </summary>
internal static partial class ErrorDialogs
{
    private const uint SemFailCriticalErrors = 0x0001;
    private const uint SemNoGpFaultErrorBox = 0x0002;

    /// <summary>Adds the two flags to the process's error mode (children inherit it); returns the mode it had before.</summary>
    public static uint Suppress()
    {
        var previous = SetErrorMode(GetErrorMode() | SemFailCriticalErrors | SemNoGpFaultErrorBox);
        return previous;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint SetErrorMode(uint mode);

    [LibraryImport("kernel32.dll")]
    private static partial uint GetErrorMode();
}
