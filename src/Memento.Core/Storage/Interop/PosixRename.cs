using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Memento.Core.Storage.Interop;

/// <summary>
/// Renames a file over another with POSIX semantics (<c>SetFileInformationByHandle</c> with <c>FileRenameInfoEx</c>,
/// Windows 10 1709 and later on NTFS). Unlike <c>MoveFileEx</c>, the target's name is replaced even while readers hold
/// it open with <c>FILE_SHARE_DELETE</c>; they keep reading the old file until they close it. A reader without
/// <c>FILE_SHARE_DELETE</c> still blocks the rename, as with any delete.
/// </summary>
internal static class PosixRename
{
    internal const int ErrorSuccess = 0;

    private const uint Delete = 0x0001_0000;
    private const uint Synchronize = 0x0010_0000;
    private const uint FileShareReadWriteDelete = 0x7;
    private const uint OpenExisting = 3;
    private const int FileRenameInfoEx = 22;
    private const uint FileRenameFlagReplaceIfExists = 0x1;
    private const uint FileRenameFlagPosixSemantics = 0x2;

    // MAX_PATH; longer paths need the \\?\ prefix because Memento does not rely on the long-path opt-in.
    private const int MaxPath = 260;

    /// <summary>Renames <paramref name="source"/> to <paramref name="destination"/>, replacing it if it exists.</summary>
    /// <returns><see cref="ErrorSuccess"/>, or the Win32 error code Windows reported.</returns>
    /// <remarks>
    /// A read-only target is not replaced (<c>ERROR_ACCESS_DENIED</c>), the same as <see cref="File.Move(string, string, bool)"/>.
    /// File systems without POSIX renames (FAT, exFAT, many network shares) and older Windows report
    /// <c>ERROR_INVALID_PARAMETER</c> or <c>ERROR_NOT_SUPPORTED</c>.
    /// </remarks>
    public static int Replace(string source, string destination)
    {
        using var handle = CreateFile(
            ToWin32Path(source), Delete | Synchronize, FileShareReadWriteDelete, IntPtr.Zero, OpenExisting, 0, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            return Marshal.GetLastWin32Error();
        }

        var information = RenameInformation(ToWin32Path(destination), FileRenameFlagReplaceIfExists | FileRenameFlagPosixSemantics);
        return SetFileInformationByHandle(handle, FileRenameInfoEx, information, (uint)information.Length)
            ? ErrorSuccess
            : Marshal.GetLastWin32Error();
    }

    /// <summary>
    /// <c>FILE_RENAME_INFO</c> with its variable-length name: a 32-bit flags union, a <c>RootDirectory</c> handle aligned
    /// to the pointer size (left null: the name is a full path), the name's length in bytes, then the UTF-16 name and a
    /// terminating null.
    /// </summary>
    internal static byte[] RenameInformation(string fullPath, uint flags)
    {
        var rootDirectoryOffset = IntPtr.Size;
        var nameLengthOffset = rootDirectoryOffset + IntPtr.Size;
        var nameOffset = nameLengthOffset + sizeof(uint);
        var nameBytes = Encoding.Unicode.GetByteCount(fullPath);

        var buffer = new byte[nameOffset + nameBytes + sizeof(char)];
        BinaryPrimitives.WriteUInt32LittleEndian(buffer, flags);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.AsSpan(nameLengthOffset), (uint)nameBytes);
        Encoding.Unicode.GetBytes(fullPath, buffer.AsSpan(nameOffset));
        return buffer;
    }

    internal static string ToWin32Path(string path)
    {
        var full = Path.GetFullPath(path);
        if (full.Length < MaxPath || full.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return full;
        }

        return full.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + full[2..] : @"\\?\" + full;
    }

#pragma warning disable SYSLIB1054 // Blittable arguments; the built-in marshaller avoids unsafe code in Core.
    [DllImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle file, int informationClass, byte[] information, uint bufferSize);
#pragma warning restore SYSLIB1054
}
