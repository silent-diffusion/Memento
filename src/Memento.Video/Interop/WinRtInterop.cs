using System.Runtime.InteropServices;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX.Direct3D11;
using WinRT;

namespace Memento.Video.Interop;

/// <summary>
/// The few COM calls between Direct3D 11 and the WinRT capture API that the SDK projection does not cover: creating a
/// capture item for a monitor or window, wrapping a D3D device for the frame pool, reaching a frame's texture, and the
/// Windows 11 capture-border switch. Called through function pointers so no COM interop marshalling is involved.
/// </summary>
internal static unsafe class WinRtInterop
{
    private static readonly Guid IidGraphicsCaptureItemInterop = new("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356");
    private static readonly Guid IidGraphicsCaptureItem = new("79C3F95B-31F7-4EC2-A464-632EF5D30760");
    private static readonly Guid IidDxgiInterfaceAccess = new("A9B3D012-3DF2-4EE3-B8D1-8695F457D3C1");
    private static readonly Guid IidGraphicsCaptureSession3 = new("F2CDD966-22AE-5EA1-9596-3A289344C3BE");
    private static readonly Guid IidGraphicsCaptureSession2 = new("2C39AE40-7D2E-5044-804E-8B6799D4CF9E");

    /// <summary>A capture item for a monitor (<c>HMONITOR</c>).</summary>
    public static GraphicsCaptureItem CreateItemForMonitor(IntPtr monitor) => CreateItem(monitor, slot: 4);

    /// <summary>A capture item for a top-level window (<c>HWND</c>).</summary>
    public static GraphicsCaptureItem CreateItemForWindow(IntPtr window) => CreateItem(window, slot: 3);

    private static GraphicsCaptureItem CreateItem(IntPtr handle, int slot)
    {
        using var factory = ActivationFactory.Get("Windows.Graphics.Capture.GraphicsCaptureItem");
        var iidInterop = IidGraphicsCaptureItemInterop;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(factory.ThisPtr, ref iidInterop, out var interop));
        try
        {
            var vtable = *(IntPtr**)interop;
            var create = (delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, int>)vtable[slot];
            var iid = IidGraphicsCaptureItem;
            IntPtr item;
            Marshal.ThrowExceptionForHR(create(interop, handle, &iid, &item));
            try
            {
                return GraphicsCaptureItem.FromAbi(item);
            }
            finally
            {
                Marshal.Release(item);
            }
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    /// <summary>Wraps a DXGI device (<c>IDXGIDevice*</c>) as the WinRT device the frame pool takes.</summary>
    public static IDirect3DDevice CreateDirect3DDevice(IntPtr dxgiDevice)
    {
        Marshal.ThrowExceptionForHR(CreateDirect3D11DeviceFromDXGIDevice(dxgiDevice, out var inspectable));
        try
        {
            return MarshalInterface<IDirect3DDevice>.FromAbi(inspectable);
        }
        finally
        {
            Marshal.Release(inspectable);
        }
    }

    /// <summary>The D3D11 texture behind a captured surface, as an owned (AddRef'd) <c>ID3D11Texture2D*</c>.</summary>
    public static IntPtr GetTexture(IDirect3DSurface surface, Guid textureIid)
    {
        var unknown = MarshalInterface<IDirect3DSurface>.FromManaged(surface);
        try
        {
            var iidAccess = IidDxgiInterfaceAccess;
            Marshal.ThrowExceptionForHR(Marshal.QueryInterface(unknown, ref iidAccess, out var access));
            try
            {
                var vtable = *(IntPtr**)access;
                var getInterface = (delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)vtable[3];
                IntPtr texture;
                Marshal.ThrowExceptionForHR(getInterface(access, &textureIid, &texture));
                return texture;
            }
            finally
            {
                Marshal.Release(access);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    /// <summary>
    /// Turns the yellow capture border off (Windows 11: <c>IGraphicsCaptureSession3.IsBorderRequired</c>). Returns false
    /// when Windows does not have the switch or refuses it; the border then stays, which is harmless.
    /// </summary>
    public static bool TrySetBorderRequired(GraphicsCaptureSession session, bool required) =>
        TrySetBool(session, IidGraphicsCaptureSession3, slot: 7, required);

    /// <summary>Sets <c>IsCursorCaptureEnabled</c> (Windows 10 2004+, <c>IGraphicsCaptureSession2</c>).</summary>
    public static bool TrySetCursorCapture(GraphicsCaptureSession session, bool enabled) =>
        TrySetBool(session, IidGraphicsCaptureSession2, slot: 7, enabled);

    private static bool TrySetBool(GraphicsCaptureSession session, Guid iid, int slot, bool value)
    {
        var unknown = MarshalInspectable<GraphicsCaptureSession>.FromManaged(session);
        try
        {
            if (Marshal.QueryInterface(unknown, ref iid, out var typed) < 0)
            {
                return false;
            }

            try
            {
                // IInspectable has six slots; the interface's getter is slot 6 and its setter slot 7.
                var vtable = *(IntPtr**)typed;
                var put = (delegate* unmanaged[Stdcall]<IntPtr, byte, int>)vtable[slot];
                return put(typed, value ? (byte)1 : (byte)0) >= 0;
            }
            finally
            {
                Marshal.Release(typed);
            }
        }
        finally
        {
            Marshal.Release(unknown);
        }
    }

    [DllImport("d3d11.dll", ExactSpelling = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr graphicsDevice);
}
