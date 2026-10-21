using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Memento.AI.Local;

/// <summary><see cref="ISystemMemoryProbe"/> from <c>GlobalMemoryStatusEx</c> (free and standby pages, as Task Manager's "Available").</summary>
public sealed partial class SystemMemoryProbe : ISystemMemoryProbe
{
    public long? AvailableBytes()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        return GlobalMemoryStatusEx(ref status) ? (long)status.AvailPhys : null;
    }

    public long WorkingSetBytes()
    {
        using var process = Process.GetCurrentProcess();
        return process.WorkingSet64;
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }
}
