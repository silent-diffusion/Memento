using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Memento.Core.Workers.Interop;

/// <summary>
/// A Windows job object with "kill on close": every process assigned to it is ended by Windows when the job's last
/// handle closes, which happens when Memento exits for any reason (closed, crashed or killed). Worker processes are
/// assigned to it, so none outlives the app. "Die on unhandled exception" makes a worker that crashes in native code end
/// at once instead of waiting on a Windows Error Reporting dialog that nobody sees (and holding the graphics card).
/// No memory limit is set: a model's needs vary, and the engines report their own allocation failures.
/// </summary>
internal sealed class JobObject : IDisposable
{
    internal const uint JobObjectLimitDieOnUnhandledException = 0x400;
    internal const uint JobObjectLimitKillOnJobClose = 0x2000;

    private const int JobObjectExtendedLimitInformation = 9;

    private readonly SafeFileHandle _handle;

    public JobObject()
    {
        _handle = CreateJobObject(IntPtr.Zero, null);
        if (_handle.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not create a job object for the workers.");
        }

        var info = new ExtendedLimitInformation { BasicLimitInformation = new BasicLimitInformation { LimitFlags = JobObjectLimitKillOnJobClose | JobObjectLimitDieOnUnhandledException } };
        var size = Marshal.SizeOf<ExtendedLimitInformation>();
        if (!SetInformationJobObject(_handle, JobObjectExtendedLimitInformation, ref info, (uint)size))
        {
            var error = Marshal.GetLastWin32Error();
            _handle.Dispose();
            throw new Win32Exception(error, "Windows did not set the job object to end its processes with Memento.");
        }
    }

    /// <summary>Puts <paramref name="process"/> in the job.</summary>
    /// <exception cref="Win32Exception">Windows refused (the process has exited, or access was denied).</exception>
    public void Assign(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        if (!AssignProcessToJobObject(_handle, process.Handle))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not put the worker in the job object.");
        }
    }

    /// <summary>Whether <paramref name="process"/> is in this job.</summary>
    public bool Contains(Process process)
    {
        ArgumentNullException.ThrowIfNull(process);
        return IsProcessInJob(process.Handle, _handle, out var result) && result;
    }

    /// <summary>The job's limit flags as Windows reports them.</summary>
    /// <exception cref="Win32Exception">Windows did not answer.</exception>
    internal uint QueryLimitFlags()
    {
        var size = (uint)Marshal.SizeOf<ExtendedLimitInformation>();
        if (!QueryInformationJobObject(_handle, JobObjectExtendedLimitInformation, out var info, size, out _))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Windows did not report the job object's limits.");
        }

        return info.BasicLimitInformation.LimitFlags;
    }

    /// <summary>Closes the job: Windows ends every process still in it.</summary>
    public void Dispose() => _handle.Dispose();

#pragma warning disable SYSLIB1054 // Blittable structs passed by reference; the built-in marshaller avoids unsafe code in Core.
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(SafeFileHandle job, int infoClass, ref ExtendedLimitInformation info, uint length);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryInformationJobObject(SafeFileHandle job, int infoClass, out ExtendedLimitInformation info, uint length, out uint returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsProcessInJob(IntPtr process, SafeFileHandle job, [MarshalAs(UnmanagedType.Bool)] out bool result);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    private struct BasicLimitInformation
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct IoCounters
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ExtendedLimitInformation
    {
        public BasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }
}
