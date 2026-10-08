using System.Runtime.InteropServices;

namespace Memento.Core.Engines.Interop;

/// <summary>
/// What the display driver says an adapter is (<c>D3DKMTQueryAdapterInfo(KMTQAITYPE_ADAPTERTYPE)</c>): on a laptop with
/// hybrid graphics the integrated GPU is flagged <c>HybridIntegrated</c> and the separate card <c>HybridDiscrete</c>. That
/// beats the memory heuristic for APUs whose firmware reserves 2 GB or more for the integrated GPU.
/// </summary>
internal static class D3dkmtAdapterType
{
    private const int KmtqaiTypeAdapterType = 15;
    private const int HybridDiscreteBit = 1 << 4;
    private const int HybridIntegratedBit = 1 << 5;

    /// <summary><c>true</c> for a hybrid discrete card, <c>false</c> for a hybrid integrated GPU, <c>null</c> when the driver does not say.</summary>
    public static bool? IsHybridDiscrete(uint luidLowPart, int luidHighPart)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            var open = new OpenAdapterFromLuid { LuidLowPart = luidLowPart, LuidHighPart = luidHighPart };
            if (D3DKMTOpenAdapterFromLuid(ref open) != 0)
            {
                return null;
            }

            var buffer = Marshal.AllocHGlobal(sizeof(uint));
            try
            {
                var query = new QueryAdapterInfo { Adapter = open.Adapter, Type = KmtqaiTypeAdapterType, PrivateDriverData = buffer, PrivateDriverDataSize = sizeof(uint) };
                if (D3DKMTQueryAdapterInfo(ref query) != 0)
                {
                    return null;
                }

                var flags = Marshal.ReadInt32(buffer);
                return (flags & HybridIntegratedBit) != 0 ? false
                    : (flags & HybridDiscreteBit) != 0 ? true
                    : null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
                var close = new CloseAdapter { Adapter = open.Adapter };
                _ = D3DKMTCloseAdapter(ref close);
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

#pragma warning disable SYSLIB1054 // Blittable structs; the built-in marshaller avoids unsafe code in Core.
    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTOpenAdapterFromLuid(ref OpenAdapterFromLuid open);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTQueryAdapterInfo(ref QueryAdapterInfo query);

    [DllImport("gdi32.dll", ExactSpelling = true)]
    private static extern int D3DKMTCloseAdapter(ref CloseAdapter close);
#pragma warning restore SYSLIB1054

    [StructLayout(LayoutKind.Sequential)]
    private struct OpenAdapterFromLuid
    {
        public uint LuidLowPart;
        public int LuidHighPart;
        public uint Adapter;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct QueryAdapterInfo
    {
        public uint Adapter;
        public int Type;
        public IntPtr PrivateDriverData;
        public uint PrivateDriverDataSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CloseAdapter
    {
        public uint Adapter;
    }
}
