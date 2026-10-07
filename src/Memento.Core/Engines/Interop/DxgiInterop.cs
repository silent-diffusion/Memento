using System.Runtime.InteropServices;

namespace Memento.Core.Engines.Interop;

/// <summary>
/// The few DXGI calls the probe needs: enumerate adapters (<c>IDXGIFactory1::EnumAdapters1</c>), read their
/// description (<c>IDXGIAdapter1::GetDesc1</c>) and the free local video memory
/// (<c>IDXGIAdapter3::QueryVideoMemoryInfo</c>). The interfaces are flattened in vtable order; slots this code never
/// calls are placeholders.
/// </summary>
internal static class DxgiInterop
{
    public const int DxgiErrorNotFound = unchecked((int)0x887A0002);
    public const uint AdapterFlagSoftware = 2;
    public const int MemorySegmentGroupLocal = 0;

    public static readonly Guid FactoryIid = new("770aae78-f26f-4dba-a829-253c83d1b387");

    [DllImport("dxgi.dll", ExactSpelling = true, PreserveSig = true)]
#pragma warning disable SYSLIB1054 // The COM factory needs the built-in marshaller.
    public static extern int CreateDXGIFactory1(ref Guid riid, [MarshalAs(UnmanagedType.Interface)] out IDxgiFactory1 factory);
#pragma warning restore SYSLIB1054

    [ComImport]
    [Guid("770aae78-f26f-4dba-a829-253c83d1b387")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDxgiFactory1
    {
        // IDXGIObject
        void SetPrivateData();

        void SetPrivateDataInterface();

        void GetPrivateData();

        void GetParent();

        // IDXGIFactory
        void EnumAdapters();

        void MakeWindowAssociation();

        void GetWindowAssociation();

        void CreateSwapChain();

        void CreateSoftwareAdapter();

        // IDXGIFactory1
        [PreserveSig]
        int EnumAdapters1(uint index, [MarshalAs(UnmanagedType.Interface)] out IDxgiAdapter3 adapter);
    }

    [ComImport]
    [Guid("645967a4-1392-4310-a798-8053ce3e93fd")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IDxgiAdapter3
    {
        // IDXGIObject
        void SetPrivateData();

        void SetPrivateDataInterface();

        void GetPrivateData();

        void GetParent();

        // IDXGIAdapter
        void EnumOutputs();

        void GetDesc();

        void CheckInterfaceSupport();

        // IDXGIAdapter1
        [PreserveSig]
        int GetDesc1(out AdapterDesc1 desc);

        // IDXGIAdapter2
        void GetDesc2();

        // IDXGIAdapter3
        void RegisterHardwareContentProtectionTeardownStatusEvent();

        void UnregisterHardwareContentProtectionTeardownStatus();

        [PreserveSig]
        int QueryVideoMemoryInfo(uint nodeIndex, int memorySegmentGroup, out VideoMemoryInfo info);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct AdapterDesc1
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string Description;
        public uint VendorId;
        public uint DeviceId;
        public uint SubSysId;
        public uint Revision;
        public nuint DedicatedVideoMemory;
        public nuint DedicatedSystemMemory;
        public nuint SharedSystemMemory;
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct VideoMemoryInfo
    {
        public ulong Budget;
        public ulong CurrentUsage;
        public ulong AvailableForReservation;
        public ulong CurrentReservation;
    }
}
