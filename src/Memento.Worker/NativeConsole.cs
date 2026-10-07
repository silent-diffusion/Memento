using System.Runtime.InteropServices;

namespace Memento.Worker;

/// <summary>Points the process's stdout handle at stderr, so text native libraries print never mixes with the protocol.</summary>
internal static partial class NativeConsole
{
    private const int StdOutputHandle = -11;
    private const int StdErrorHandle = -12;

    public static void RedirectStdoutToStderr()
    {
        var error = GetStdHandle(StdErrorHandle);
        if (error != IntPtr.Zero && error != new IntPtr(-1))
        {
            SetStdHandle(StdOutputHandle, error);
        }
    }

    [LibraryImport("kernel32.dll", SetLastError = true)]
    private static partial IntPtr GetStdHandle(int handle);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetStdHandle(int handle, IntPtr value);
}
