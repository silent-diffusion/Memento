namespace Memento.Video.Sources;

/// <summary>
/// How a camera is reached (BRIDGE.md, <c>VideoSource.backend</c>): Media Foundation for webcams and capture cards
/// Windows lists as cameras, DirectShow for the video input filters Media Foundation does not see (the OBS Virtual
/// Camera, NDI and other virtual or software cameras, DirectShow-only capture cards). ENGINE-NOTES.md §R.
/// </summary>
public enum CameraBackend
{
    MediaFoundation,
    DirectShow,
}
