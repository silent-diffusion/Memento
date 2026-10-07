using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

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

    /// <summary>
    /// Resolves "@%SystemRoot%\System32\x.dll,-202"-style display names; null if it cannot, or if the string points
    /// anywhere but an installed package or a module under Windows or Program Files (see <see cref="IsLocalResource(string)"/>).
    /// </summary>
    public static string? LoadIndirectString(string source)
    {
        // The display name comes from another process's audio session: resolving "@\\host\share\x.dll,-1" would make
        // Memento open a file on that host (and send this user's NTLM credentials to it).
        if (!IsLocalResource(source))
        {
            return null;
        }

        var buffer = stackalloc char[512];
        if (SHLoadIndirectString(source, buffer, 512, IntPtr.Zero) != 0)
        {
            return null;
        }

        return new string(buffer);
    }

    /// <summary><see cref="IsLocalResource(string, IReadOnlyList{string})"/> with the Windows and Program Files folders.</summary>
    public static bool IsLocalResource(string source) => IsLocalResource(source, TrustedResourceRoots);

    /// <summary>
    /// An indirect string Memento may resolve: a package resource (<c>@{PackageFullName?ms-resource:…}</c>, a package
    /// name and no path), or <c>@&lt;path&gt;,&lt;id&gt;</c> whose path, after expanding environment variables, is a
    /// fully qualified local path (no UNC, device or relative path) inside one of <paramref name="trustedRoots"/>.
    /// </summary>
    public static bool IsLocalResource(string source, IReadOnlyList<string> trustedRoots)
    {
        ArgumentNullException.ThrowIfNull(trustedRoots);
        if (string.IsNullOrEmpty(source) || source.Length > 1024 || source[0] != '@' || source.Contains('\0', StringComparison.Ordinal))
        {
            return false;
        }

        var body = source[1..];
        if (body.StartsWith('{'))
        {
            // "@{C:\x\resources.pri?…}" and "@{\\host\share\x.pri?…}" name a file: only a package's full name is accepted.
            return PackageResource().IsMatch(source);
        }

        var comma = body.LastIndexOf(',');
        if (comma <= 0 || !ResourceId().IsMatch(body[(comma + 1)..]))
        {
            return false;
        }

        try
        {
            var path = Environment.ExpandEnvironmentVariables(body[..comma]);
            if (path.Contains('%', StringComparison.Ordinal) || path.StartsWith(@"\\", StringComparison.Ordinal) || path.StartsWith("//", StringComparison.Ordinal)
                || path.StartsWith(@"\/", StringComparison.Ordinal) || path.StartsWith(@"/\", StringComparison.Ordinal) || !Path.IsPathFullyQualified(path))
            {
                return false;
            }

            var full = Path.GetFullPath(path);
            return !full.StartsWith(@"\\", StringComparison.Ordinal)
                && trustedRoots.Any(root => !string.IsNullOrEmpty(root) && Path.IsPathFullyQualified(root) && IsUnder(full, root));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool IsUnder(string path, string root)
    {
        var prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        return path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> TrustedResourceRoots =>
    [
        Environment.GetFolderPath(Environment.SpecialFolder.Windows),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
    ];

    [GeneratedRegex(@"^@\{[A-Za-z0-9._~\-]+\?ms-resource:[^\\{}]*\}$", RegexOptions.CultureInvariant)]
    private static partial Regex PackageResource();

    /// <summary>"-202", "202", optionally with a version modifier: "-202;v2".</summary>
    [GeneratedRegex(@"^-?[0-9]{1,9}(;v[0-9]{1,9})?$", RegexOptions.CultureInvariant)]
    private static partial Regex ResourceId();

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
