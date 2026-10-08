using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Memento.Core.Engines.Interop;

/// <summary>
/// <see cref="IProcessDirectory"/> from one Toolhelp snapshot (ids, parents, executable names), with the start time and
/// file description read only for the processes asked about (<c>PROCESS_QUERY_LIMITED_INFORMATION</c>, which works for
/// other users' processes too). Nothing here throws: a process that cannot be read keeps its snapshot name.
/// </summary>
internal sealed class WindowsProcessDirectory : IProcessDirectory
{
    private const uint SnapProcess = 0x00000002;
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const int MaxCachedDescriptions = 256;
    private static readonly IntPtr InvalidHandle = new(-1);

    /// <summary>File descriptions by executable path; they do not change while the file exists.</summary>
    private static readonly ConcurrentDictionary<string, string?> Descriptions = new(StringComparer.OrdinalIgnoreCase);

    private readonly Dictionary<int, (int Parent, string Exe)> _snapshot;
    private readonly Dictionary<int, ProcessEntry?> _resolved = [];

    private WindowsProcessDirectory(Dictionary<int, (int Parent, string Exe)> snapshot)
    {
        _snapshot = snapshot;
    }

    /// <summary>The processes running now, or <c>null</c> when Windows would not list them.</summary>
    public static WindowsProcessDirectory? Take()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var handle = CreateToolhelp32Snapshot(SnapProcess, 0);
            if (handle == InvalidHandle || handle == IntPtr.Zero)
            {
                return null;
            }

            try
            {
                var snapshot = new Dictionary<int, (int, string)>();
                var entry = new ProcessEntry32 { Size = (uint)Marshal.SizeOf<ProcessEntry32>() };
                for (var more = Process32FirstW(handle, ref entry); more; more = Process32NextW(handle, ref entry))
                {
                    snapshot[(int)entry.ProcessId] = ((int)entry.ParentProcessId, entry.ExeFile ?? string.Empty);
                }

                return new WindowsProcessDirectory(snapshot);
            }
            finally
            {
                _ = CloseHandle(handle);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    public ProcessEntry? Find(int pid)
    {
        if (_resolved.TryGetValue(pid, out var known))
        {
            return known;
        }

        ProcessEntry? result = null;
        if (_snapshot.TryGetValue(pid, out var row))
        {
            var (path, started) = Inspect(pid);
            result = new ProcessEntry(pid, row.Parent, row.Exe, path is null ? null : Description(path), started);
        }

        _resolved[pid] = result;
        return result;
    }

    private static (string? Path, DateTime? Started) Inspect(int pid)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)pid);
        if (handle == IntPtr.Zero)
        {
            return (null, null);
        }

        try
        {
            DateTime? started = GetProcessTimes(handle, out var creation, out _, out _, out _) && creation > 0
                ? DateTime.FromFileTimeUtc(creation)
                : null;
            var buffer = new char[1024];
            var length = (uint)buffer.Length;
            var path = QueryFullProcessImageNameW(handle, 0, buffer, ref length) ? new string(buffer, 0, (int)length) : null;
            return (path, started);
        }
        finally
        {
            _ = CloseHandle(handle);
        }
    }

    private static string? Description(string path)
    {
        if (Descriptions.TryGetValue(path, out var cached))
        {
            return cached;
        }

        string? description;
        try
        {
            description = FileVersionInfo.GetVersionInfo(path).FileDescription?.Trim();
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException or UnauthorizedAccessException or ArgumentException)
        {
            description = null;
        }

        if (Descriptions.Count >= MaxCachedDescriptions)
        {
            Descriptions.Clear();
        }

        Descriptions[path] = string.IsNullOrEmpty(description) ? null : description;
        return Descriptions[path];
    }

#pragma warning disable SYSLIB1054 // Blittable arguments and UTF-16 strings; the built-in marshaller avoids unsafe code in Core.
    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32FirstW(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Process32NextW(IntPtr snapshot, ref ProcessEntry32 entry);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetProcessTimes(IntPtr process, out long creation, out long exit, out long kernel, out long user);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool QueryFullProcessImageNameW(IntPtr process, uint flags, [Out] char[] name, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ProcessEntry32
    {
        public uint Size;
        public uint Usage;
        public uint ProcessId;
        public IntPtr DefaultHeapId;
        public uint ModuleId;
        public uint Threads;
        public uint ParentProcessId;
        public int PriorityClassBase;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string ExeFile;
    }
}
