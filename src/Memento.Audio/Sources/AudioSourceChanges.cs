namespace Memento.Audio.Sources;

/// <summary>What changed since the last <see cref="AudioSourceWatcher.Changed"/> event (debounced, so several may be set).</summary>
[Flags]
public enum AudioSourceChanges
{
    None = 0,
    DeviceAdded = 0x1,
    DeviceRemoved = 0x2,
    DeviceStateChanged = 0x4,
    DefaultDeviceChanged = 0x8,
    SessionCreated = 0x10,
}
