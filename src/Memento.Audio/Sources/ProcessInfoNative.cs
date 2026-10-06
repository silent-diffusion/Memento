using System.Runtime.InteropServices;

namespace Memento.Audio.Sources;

/// <summary>Process image path and indirect-string resolution for processes .NET cannot inspect.</summary>
internal static unsafe partial class ProcessInfoNative
{
    private const uint ProcessQueryLimitedInformation = 0x1000;

    /// <summary><c>QueryFullProcessImageName</c>: works for elevated and protected processes whose modules cannot be enumerated.</summary>
    public static string? QueryImagePath(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            var buffer = stackalloc char[1024];
            var size = 1024u;
            return QueryFullProcessImageNameW(handle, 0, buffer, ref size) ? new string(buffer, 0, (int)size) : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>The process that created <paramref name="processId"/>, or null if it cannot be read.</summary>
    public static int? ParentProcessId(int processId)
    {
        var handle = OpenProcess(ProcessQueryLimitedInformation, false, (uint)processId);
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            ProcessBasicInformation info;
            return NtQueryInformationProcess(handle, 0, &info, (uint)sizeof(ProcessBasicInformation), out _) == 0
                ? (int)info.InheritedFromUniqueProcessId.ToInt64() // Process ids fit in 32 bits.
                : null;
        }
        finally
        {
            CloseHandle(handle);
        }
    }

    /// <summary>
    /// Whether <paramref name="processId"/> is <paramref name="ancestorId"/> or was started by it (directly or through a
    /// few levels), e.g. the WebView2 browser and audio processes Memento itself runs.
    /// </summary>
    public static bool IsInTreeOf(int processId, int ancestorId)
    {
        var current = processId;
        for (var depth = 0; depth < 8; depth++)
        {
            if (current == ancestorId)
            {
                return true;
            }

            if (ParentProcessId(current) is not { } parent || parent <= 4 || parent == current)
            {
                return false;
            }

            current = parent;
        }

        return false;
    }

    /// <summary>Resolves "@%SystemRoot%\System32\x.dll,-202"-style display names; null if it cannot.</summary>
    public static string? LoadIndirectString(string source)
    {
        var buffer = stackalloc char[512];
        if (SHLoadIndirectString(source, buffer, 512, IntPtr.Zero) != 0)
        {
            return null;
        }

        return new string(buffer);
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr OpenProcess(uint desiredAccess, [MarshalAs(UnmanagedType.Bool)] bool inheritHandle, uint processId);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool CloseHandle(IntPtr handle);

    [LibraryImport("kernel32.dll", SetLastError = true, EntryPoint = "QueryFullProcessImageNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool QueryFullProcessImageNameW(IntPtr process, uint flags, char* exeName, ref uint size);

    [LibraryImport("shlwapi.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHLoadIndirectString(string source, char* outBuffer, uint outBufferSize, IntPtr reserved);

    [LibraryImport("ntdll.dll")]
    private static partial int NtQueryInformationProcess(IntPtr process, int informationClass, ProcessBasicInformation* information, uint length, out uint returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr ExitStatus;
        public IntPtr PebBaseAddress;
        public IntPtr AffinityMask;
        public IntPtr BasePriority;
        public IntPtr UniqueProcessId;
        public IntPtr InheritedFromUniqueProcessId;
    }
}
