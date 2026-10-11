using Memento.Video.Sources;

namespace Memento.Video.Tests.Fakes;

/// <summary>A DirectShow camera without DirectShow: pictures are pushed by the test on its own thread, as the streaming thread would.</summary>
internal sealed class FakeFrameFeed(RawVideoFormat format) : IRawFrameFeed
{
    private RawFrameHandler? _onFrame;
    private Action<int>? _onError;

    public RawVideoFormat Format { get; } = format;

    public bool Started { get; private set; }

    public bool Stopped { get; private set; }

    public bool Disposed { get; private set; }

    public VideoCaptureException? FailOnStart { get; set; }

    public void Start(RawFrameHandler onFrame, Action<int> onError)
    {
        if (FailOnStart is not null)
        {
            throw FailOnStart;
        }

        _onFrame = onFrame;
        _onError = onError;
        Started = true;
    }

    public unsafe void Push(byte[] picture)
    {
        fixed (byte* data = picture)
        {
            _onFrame!((IntPtr)data, picture.Length);
        }
    }

    public void Fail(int hresult) => _onError!(hresult);

    public void Stop() => Stopped = true;

    public void Dispose() => Disposed = true;
}
