using System.Runtime.InteropServices;
using Memento.Audio;
using Microsoft.Win32.SafeHandles;

namespace Memento.Video.Recording;

/// <summary>
/// Sleeps until a QPC time with sub-millisecond precision (a high-resolution waitable timer, Windows 10 1803+), without
/// raising the system timer resolution. Falls back to <see cref="Thread.Sleep(int)"/> where the flag is not supported.
/// </summary>
internal sealed class HighResolutionWaiter : IDisposable
{
    private const uint CreateWaitableTimerHighResolution = 0x00000002;
    private const uint TimerAllAccess = 0x1F0003;
    private readonly SafeWaitHandle? _timer;
    private TimerWaitHandle? _timerHandle;

    public HighResolutionWaiter()
    {
        var handle = CreateWaitableTimerExW(IntPtr.Zero, null, CreateWaitableTimerHighResolution, TimerAllAccess);
        _timer = handle == IntPtr.Zero ? null : new SafeWaitHandle(handle, ownsHandle: true);
    }

    /// <summary>Waits until <paramref name="dueQpc"/> or until <paramref name="cancel"/> is set.</summary>
    public void WaitUntil(long dueQpc, WaitHandle cancel)
    {
        ArgumentNullException.ThrowIfNull(cancel);
        var remaining = dueQpc - QpcClock.Now;
        if (remaining <= 0)
        {
            return;
        }

        if (_timer is null)
        {
            cancel.WaitOne(TimeSpan.FromTicks(remaining));
            return;
        }

        // Negative = relative, in 100 ns units.
        var relative = -remaining;
        if (!SetWaitableTimer(_timer, ref relative, 0, IntPtr.Zero, IntPtr.Zero, false))
        {
            cancel.WaitOne(TimeSpan.FromTicks(remaining));
            return;
        }

        _timerHandle ??= new TimerWaitHandle(_timer);
        WaitHandle.WaitAny([_timerHandle, cancel]);
    }

    public void Dispose()
    {
        _timerHandle?.Dispose();
        _timer?.Dispose();
    }

    private sealed class TimerWaitHandle : WaitHandle
    {
        public TimerWaitHandle(SafeWaitHandle handle)
        {
            SafeWaitHandle = new SafeWaitHandle(handle.DangerousGetHandle(), ownsHandle: false);
        }
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern IntPtr CreateWaitableTimerExW(IntPtr attributes, string? name, uint flags, uint access);

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWaitableTimer(SafeWaitHandle timer, ref long dueTime, int period, IntPtr completion, IntPtr argument, [MarshalAs(UnmanagedType.Bool)] bool resume);
}
