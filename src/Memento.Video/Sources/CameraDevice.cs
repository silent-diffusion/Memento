namespace Memento.Video.Sources;

/// <summary>
/// A camera as one backend lists it, before the two lists are merged (<see cref="CameraListMerger"/>).
/// </summary>
/// <param name="Name">The friendly name ("Logitech Brio", "OBS Virtual Camera").</param>
/// <param name="Key">The id key: Media Foundation's symbolic link, or a DirectShow key (<see cref="CameraListMerger.DirectShowKey"/>).</param>
/// <param name="DevicePath">The device interface path (<c>\\?\usb#vid_…</c>) when the device has one; virtual cameras have none.</param>
public sealed record CameraDevice(string Name, string Key, string? DevicePath);
