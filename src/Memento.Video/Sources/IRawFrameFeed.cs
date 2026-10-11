namespace Memento.Video.Sources;

/// <summary>
/// Pictures in system memory from a camera that is not read through Media Foundation: a DirectShow graph
/// (<see cref="DirectShowGraph"/>) in the app, a scripted feed in tests. Opening it chooses the format; pictures arrive
/// on the feed's own thread once started.
/// </summary>
internal interface IRawFrameFeed : IDisposable
{
    /// <summary>What each picture holds, fixed once opened.</summary>
    RawVideoFormat Format { get; }

    /// <summary>
    /// Starts the pictures. <paramref name="onFrame"/> is called on the feed's thread with each picture (valid only during
    /// the call); <paramref name="onError"/> once with an HRESULT when the device goes away or the feed fails.
    /// Throws <see cref="VideoCaptureException"/> when the device can't start.
    /// </summary>
    void Start(RawFrameHandler onFrame, Action<int> onError);

    void Stop();
}
