using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Memento.AI.Local;

/// <summary>
/// The flag llama.cpp polls between graph nodes (<c>llama_set_abort_callback</c>), so a long prompt evaluation can be
/// stopped by cancellation or by the spill watch (about 0.4 s to stop on the reference laptop). Pinned through a
/// GCHandle passed as the callback's user data; the callback is a static unmanaged function.
/// </summary>
internal sealed unsafe class AbortState : IDisposable
{
    private GCHandle _handle;
    private volatile int _abort;

    public AbortState() => _handle = GCHandle.Alloc(this, GCHandleType.Normal);

    public static IntPtr Callback => (IntPtr)(delegate* unmanaged[Cdecl]<IntPtr, byte>)&ShouldAbort;

    public IntPtr UserData => GCHandle.ToIntPtr(_handle);

    public bool IsSet => _abort != 0;

    public void Set() => _abort = 1;

    public void Reset() => _abort = 0;

    public void Dispose()
    {
        if (_handle.IsAllocated)
        {
            _handle.Free();
        }
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
    private static byte ShouldAbort(IntPtr data)
    {
        if (data == IntPtr.Zero)
        {
            return 0;
        }

        return GCHandle.FromIntPtr(data).Target is AbortState { IsSet: true } ? (byte)1 : (byte)0;
    }
}
