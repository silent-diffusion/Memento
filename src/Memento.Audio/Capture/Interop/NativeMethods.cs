using System.Runtime.InteropServices;

namespace Memento.Audio.Capture.Interop;

internal static partial class NativeMethods
{
    [DllImport("Mmdevapi.dll", ExactSpelling = true, PreserveSig = true)]
#pragma warning disable SYSLIB1054 // COM interface parameters need the built-in marshaller.
    public static extern int ActivateAudioInterfaceAsync(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceInterfacePath,
        ref Guid riid,
        IntPtr activationParams,
        CoreAudio.IActivateAudioInterfaceCompletionHandler completionHandler,
        out CoreAudio.IActivateAudioInterfaceAsyncOperation activationOperation);
#pragma warning restore SYSLIB1054

    [LibraryImport("ole32.dll")]
    public static partial int CoCreateInstance(in Guid clsid, IntPtr outer, int clsContext, in Guid iid, out IntPtr instance);

    [LibraryImport("avrt.dll", EntryPoint = "AvSetMmThreadCharacteristicsW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    public static partial IntPtr AvSetMmThreadCharacteristics(string taskName, ref uint taskIndex);

    [LibraryImport("avrt.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AvRevertMmThreadCharacteristics(IntPtr avrtHandle);
}
