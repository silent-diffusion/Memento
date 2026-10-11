using System.Runtime.InteropServices;
using Memento.Video.Interop;

namespace Memento.Video.Sources;

/// <summary>
/// The sample grabber's <c>ISampleGrabberCB</c>: DirectShow's streaming thread calls <see cref="BufferCB"/> with each
/// picture, which goes straight to the handler (a copy into the bounded queue, nothing else). An exception must never
/// cross back into the filter, so a failing handler is caught and the picture skipped.
/// </summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class SampleGrabberCallback(RawFrameHandler handler) : DirectShow.ISampleGrabberCB
{
    private volatile bool _enabled = true;

    /// <summary>Pictures the handler threw on (none expected).</summary>
    public long Failures { get; private set; }

    public void Disable() => _enabled = false;

    public int SampleCB(double sampleTime, IntPtr sample) => DirectShow.SOk;

    public int BufferCB(double sampleTime, IntPtr buffer, int bufferLength)
    {
        if (!_enabled || buffer == IntPtr.Zero || bufferLength <= 0)
        {
            return DirectShow.SOk;
        }

        try
        {
            handler(buffer, bufferLength);
        }
#pragma warning disable CA1031 // Nothing may unwind into DirectShow's streaming thread.
        catch (Exception)
#pragma warning restore CA1031
        {
            Failures++;
        }

        return DirectShow.SOk;
    }
}
