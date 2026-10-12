using System.Runtime.InteropServices;

namespace Memento.Documents.Agenda.Text;

/// <summary>
/// The processor cycles the calling thread has used (<c>QueryThreadCycleTime</c>): a measure of work done, unlike a
/// stopwatch, which also counts the time the thread was waiting to run. Used by the parser hardening checks
/// (<see cref="WorkTally"/>).
/// </summary>
internal static class ThreadCycles
{
    /// <summary>The cycles the calling thread has used so far.</summary>
    public static long Now() => QueryThreadCycleTime(GetCurrentThread(), out var cycles) ? (long)cycles : 0;

    [DllImport("kernel32", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryThreadCycleTime(IntPtr thread, out ulong cycles);

    [DllImport("kernel32", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr GetCurrentThread();
}
