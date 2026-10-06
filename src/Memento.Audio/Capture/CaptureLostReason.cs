namespace Memento.Audio.Capture;

/// <summary>Why a capture stream ended on its own.</summary>
public enum CaptureLostReason
{
    /// <summary><c>AUDCLNT_E_DEVICE_INVALIDATED</c>: unplugged, disabled or reconfigured.</summary>
    DeviceInvalidated,

    /// <summary>The audio session was disconnected (<c>IAudioSessionEvents::OnSessionDisconnected</c>).</summary>
    SessionDisconnected,

    /// <summary>The Windows audio service stopped.</summary>
    ServiceStopped,

    /// <summary>The target application exited (application sources).</summary>
    ProcessExited,

    /// <summary>Any other WASAPI failure; see <see cref="CaptureLostEventArgs.HResult"/>.</summary>
    Error,
}
