using System.Runtime.InteropServices;

namespace Memento.Core.Engines.Interop;

/// <summary>Processor times and memory status from kernel32.</summary>
internal static class SystemInterop
{
    [DllImport("kernel32.dll", SetLastError = true)]
#pragma warning disable SYSLIB1054 // Blittable out parameters; the built-in marshaller avoids unsafe code in Core.
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    internal struct MemoryStatusEx
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
