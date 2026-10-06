using System.Runtime.InteropServices;

namespace Memento.Audio.Capture.Interop;

/// <summary>
/// Hand-written Core Audio COM interop (no CsWin32), only the members the capture loop uses.
/// Vtable order must match the Windows SDK headers exactly.
/// </summary>
internal static class CoreAudio
{
    public const int AudclntShareModeShared = 0;

    public const uint StreamFlagsLoopback = 0x0002_0000;
    public const uint StreamFlagsEventCallback = 0x0004_0000;
    public const uint StreamFlagsAutoConvertPcm = 0x8000_0000;
    public const uint StreamFlagsSrcDefaultQuality = 0x0800_0000;

    public const uint BufferFlagsDataDiscontinuity = 0x1;
    public const uint BufferFlagsSilent = 0x2;
    public const uint BufferFlagsTimestampError = 0x4;

    public const int SOk = 0;
    public const int AudclntSBufferEmpty = 0x0889_0001;
    public const int AudclntEDeviceInvalidated = unchecked((int)0x8889_0004);
    public const int AudclntEServiceNotRunning = unchecked((int)0x8889_0010);
    public const int AudclntEResourcesInvalidated = unchecked((int)0x8889_0026);
    public const int ENotFound = unchecked((int)0x8007_0490);
    public const int EAccessDenied = unchecked((int)0x8007_0005);

    public const int DeviceStateActive = 0x1;
    public const int ClsctxAll = 0x17;

    public const string ProcessLoopbackDevicePath = "VAD\\Process_Loopback";
    public const int ActivationTypeProcessLoopback = 1;
    public const int ProcessLoopbackModeIncludeTargetProcessTree = 0;
    public const ushort VtBlob = 65;

    public static readonly Guid IidAudioClient = new("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2");
    public static readonly Guid IidAudioCaptureClient = new("C8ADBD64-E71E-48a0-A4DE-185C395CD317");
    public static readonly Guid IidAudioSessionControl = new("F4B1A599-7266-4319-A8CA-E70ACB11E8CD");

    [ComImport]
    [Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    public class MMDeviceEnumeratorCoClass
    {
    }

    [ComImport]
    [Guid("A95664D2-9614-4F35-A746-DE8DB63617E6")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDeviceEnumerator
    {
        [PreserveSig]
        int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);

        [PreserveSig]
        int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice endpoint);

        [PreserveSig]
        int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);

        [PreserveSig]
        int RegisterEndpointNotificationCallback(IntPtr client);

        [PreserveSig]
        int UnregisterEndpointNotificationCallback(IntPtr client);
    }

    [ComImport]
    [Guid("D666063F-1587-4E43-81F1-B948E807363F")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IMMDevice
    {
        [PreserveSig]
        int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object instance);

        [PreserveSig]
        int OpenPropertyStore(int access, out IntPtr properties);

        [PreserveSig]
        int GetId(out IntPtr id);

        [PreserveSig]
        int GetState(out int state);
    }

    [ComImport]
    [Guid("1CB9AD4C-DBFA-4c32-B178-C2F568A703B2")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioClient
    {
        [PreserveSig]
        int Initialize(int shareMode, uint streamFlags, long bufferDuration, long periodicity, IntPtr format, IntPtr audioSessionGuid);

        [PreserveSig]
        int GetBufferSize(out uint bufferFrames);

        [PreserveSig]
        int GetStreamLatency(out long latency);

        [PreserveSig]
        int GetCurrentPadding(out uint paddingFrames);

        [PreserveSig]
        int IsFormatSupported(int shareMode, IntPtr format, out IntPtr closestMatch);

        [PreserveSig]
        int GetMixFormat(out IntPtr deviceFormat);

        [PreserveSig]
        int GetDevicePeriod(out long defaultPeriod, out long minimumPeriod);

        [PreserveSig]
        int Start();

        [PreserveSig]
        int Stop();

        [PreserveSig]
        int Reset();

        [PreserveSig]
        int SetEventHandle(IntPtr eventHandle);

        [PreserveSig]
        int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
    }

    [ComImport]
    [Guid("C8ADBD64-E71E-48a0-A4DE-185C395CD317")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioCaptureClient
    {
        [PreserveSig]
        int GetBuffer(out IntPtr data, out uint framesToRead, out uint flags, out ulong devicePosition, out ulong qpcPosition);

        [PreserveSig]
        int ReleaseBuffer(uint framesRead);

        [PreserveSig]
        int GetNextPacketSize(out uint framesInNextPacket);
    }

    [ComImport]
    [Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionControl
    {
        [PreserveSig]
        int GetState(out int state);

        [PreserveSig]
        int GetDisplayName(out IntPtr name);

        [PreserveSig]
        int SetDisplayName(IntPtr name, IntPtr eventContext);

        [PreserveSig]
        int GetIconPath(out IntPtr path);

        [PreserveSig]
        int SetIconPath(IntPtr path, IntPtr eventContext);

        [PreserveSig]
        int GetGroupingParam(out Guid groupingParam);

        [PreserveSig]
        int SetGroupingParam(IntPtr groupingParam, IntPtr eventContext);

        [PreserveSig]
        int RegisterAudioSessionNotification(IAudioSessionEvents client);

        [PreserveSig]
        int UnregisterAudioSessionNotification(IAudioSessionEvents client);
    }

    [ComImport]
    [Guid("24918ACC-64B3-37C1-8CA9-74A66E9957A8")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAudioSessionEvents
    {
        [PreserveSig]
        int OnDisplayNameChanged(IntPtr newDisplayName, IntPtr eventContext);

        [PreserveSig]
        int OnIconPathChanged(IntPtr newIconPath, IntPtr eventContext);

        [PreserveSig]
        int OnSimpleVolumeChanged(float newVolume, int newMute, IntPtr eventContext);

        [PreserveSig]
        int OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumes, uint changedChannel, IntPtr eventContext);

        [PreserveSig]
        int OnGroupingParamChanged(IntPtr newGroupingParam, IntPtr eventContext);

        [PreserveSig]
        int OnStateChanged(int newState);

        [PreserveSig]
        int OnSessionDisconnected(int disconnectReason);
    }

    [ComImport]
    [Guid("72A22D78-CDE4-431D-B8CC-843A71199B6D")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateAudioInterfaceAsyncOperation
    {
        [PreserveSig]
        int GetActivateResult(out int activateResult, [MarshalAs(UnmanagedType.IUnknown)] out object activatedInterface);
    }

    [ComImport]
    [Guid("41D949AB-9862-444A-80F6-C261334DA5EB")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IActivateAudioInterfaceCompletionHandler
    {
        [PreserveSig]
        int ActivateCompleted(IActivateAudioInterfaceAsyncOperation activateOperation);
    }

    /// <summary>Marker: the completion handler is called on an MTA worker thread and must be agile.</summary>
    [ComImport]
    [Guid("94ea2b94-e9cc-49e0-c0ff-ee64ca8f5b90")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IAgileObject
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct AudioClientActivationParams
    {
        public int ActivationType;
        public uint TargetProcessId;
        public int ProcessLoopbackMode;
    }

    /// <summary>PROPVARIANT holding a VT_BLOB (24 bytes on x64).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct PropVariantBlob
    {
        public ushort Vt;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public uint Size;
        public uint Padding;
        public IntPtr Data;
    }
}
