using System.Runtime.InteropServices;

namespace Memento.Video.Interop;

/// <summary>The window and monitor calls the source enumerator and the capture sources use.</summary>
internal static partial class User32
{
    public const uint WdaNone = 0x0;
    public const uint WdaMonitor = 0x1;
    public const uint WdaExcludeFromCapture = 0x11;

    private const uint MonitorInfoPrimary = 0x1;
    private const int DwmwaCloaked = 14;
    private const int GwlExStyle = -20;
    private const long WsExToolWindow = 0x80;
    private const long WsExNoActivate = 0x08000000;

    public delegate bool MonitorEnumProc(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data);

    public delegate bool WindowEnumProc(IntPtr hwnd, IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    public struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;

        public readonly int Width => Right - Left;

        public readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MonitorInfoEx
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string Device;
    }

    public sealed record MonitorEntry(IntPtr Handle, string DeviceName, Rect Bounds, bool IsPrimary, uint DpiX);

    public static List<MonitorEntry> Monitors()
    {
        var result = new List<MonitorEntry>();
        EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (monitor, hdc, rect, data) =>
        {
            var info = new MonitorInfoEx { Size = Marshal.SizeOf<MonitorInfoEx>() };
            if (GetMonitorInfoW(monitor, ref info))
            {
                var dpi = GetDpiForMonitor(monitor, 0, out var dpiX, out _) >= 0 ? dpiX : 96u;
                result.Add(new MonitorEntry(monitor, info.Device, info.Monitor, (info.Flags & MonitorInfoPrimary) != 0, dpi));
            }

            return true;
        }, IntPtr.Zero);
        return result;
    }

    public sealed record WindowEntry(IntPtr Handle, string Title, int ProcessId, Rect Bounds, bool IsMinimized, uint DisplayAffinity);

    /// <summary>Visible, titled, uncloaked top-level windows that are not tool windows: the ones Windows' own picker lists.</summary>
    public static List<WindowEntry> CapturableWindows()
    {
        var result = new List<WindowEntry>();
        EnumWindows((hwnd, unused) =>
        {
            if (!IsWindowVisible(hwnd) || GetAncestor(hwnd, 2) != hwnd)
            {
                return true;
            }

            var exStyle = (long)GetWindowLongPtrW(hwnd, GwlExStyle);
            if ((exStyle & WsExToolWindow) != 0 || (exStyle & WsExNoActivate) != 0)
            {
                return true;
            }

            if (DwmGetWindowAttribute(hwnd, DwmwaCloaked, out var cloaked, sizeof(int)) >= 0 && cloaked != 0)
            {
                return true;
            }

            var title = WindowTitle(hwnd);
            if (title.Length == 0)
            {
                return true;
            }

            _ = GetWindowThreadProcessId(hwnd, out var pid);
            _ = GetWindowRect(hwnd, out var rect);
            _ = GetWindowDisplayAffinity(hwnd, out var affinity);
            result.Add(new WindowEntry(hwnd, title, (int)pid, rect, IsIconic(hwnd), affinity));
            return true;
        }, IntPtr.Zero);
        return result;
    }

    public static string WindowTitle(IntPtr hwnd)
    {
        var length = GetWindowTextLengthW(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var buffer = new char[length + 1];
        var copied = GetWindowTextW(hwnd, buffer, buffer.Length);
        return new string(buffer, 0, Math.Clamp(copied, 0, length));
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct DevMode
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string DeviceName;
        public ushort SpecVersion;
        public ushort DriverVersion;
        public ushort Size;
        public ushort DriverExtra;
        public uint Fields;
        public int PositionX;
        public int PositionY;
        public uint DisplayOrientation;
        public uint DisplayFixedOutput;
        public short Color;
        public short Duplex;
        public short YResolution;
        public short TTOption;
        public short Collate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        public string FormName;
        public ushort LogPixels;
        public uint BitsPerPel;
        public uint PelsWidth;
        public uint PelsHeight;
        public uint DisplayFlags;
        public uint DisplayFrequency;
        public uint IcmMethod;
        public uint IcmIntent;
        public uint MediaType;
        public uint DitherType;
        public uint Reserved1;
        public uint Reserved2;
        public uint PanningWidth;
        public uint PanningHeight;
    }

    /// <summary>The display's current mode in physical pixels: position on the desktop, size, refresh rate.</summary>
    public static (int Left, int Top, int Width, int Height, int RefreshRate)? CurrentMode(string deviceName)
    {
        var mode = new DevMode { Size = (ushort)Marshal.SizeOf<DevMode>() };
        if (!EnumDisplaySettingsW(deviceName, -1, ref mode))
        {
            return null;
        }

        return (mode.PositionX, mode.PositionY, (int)mode.PelsWidth, (int)mode.PelsHeight, (int)mode.DisplayFrequency);
    }

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplaySettingsW(string deviceName, int modeNumber, ref DevMode mode);

    public static uint DisplayAffinity(IntPtr hwnd) => GetWindowDisplayAffinity(hwnd, out var affinity) ? affinity : WdaNone;

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clip, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfoW(IntPtr monitor, ref MonitorInfoEx info);

    [DllImport("shcore.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumWindows(WindowEnumProc callback, IntPtr data);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindowVisible(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool IsIconic(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial IntPtr GetWindowLongPtrW(IntPtr hwnd, int index);

    [LibraryImport("dwmapi.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int GetWindowTextLengthW(IntPtr hwnd);

    [DllImport("user32.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int GetWindowTextW(IntPtr hwnd, [Out] char[] text, int maxCount);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    public static partial uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);
}
