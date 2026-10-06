using System.Runtime.InteropServices;

namespace Memento.Audio.Capture.Interop;

/// <summary>Receives <c>IAudioSessionEvents</c> for a capture client and reports only a disconnect.</summary>
[ComVisible(true)]
[ClassInterface(ClassInterfaceType.None)]
internal sealed class SessionEventsSink(Action<int> onDisconnected) : CoreAudio.IAudioSessionEvents
{
    public int OnDisplayNameChanged(IntPtr newDisplayName, IntPtr eventContext) => CoreAudio.SOk;

    public int OnIconPathChanged(IntPtr newIconPath, IntPtr eventContext) => CoreAudio.SOk;

    public int OnSimpleVolumeChanged(float newVolume, int newMute, IntPtr eventContext) => CoreAudio.SOk;

    public int OnChannelVolumeChanged(uint channelCount, IntPtr newChannelVolumes, uint changedChannel, IntPtr eventContext) => CoreAudio.SOk;

    public int OnGroupingParamChanged(IntPtr newGroupingParam, IntPtr eventContext) => CoreAudio.SOk;

    public int OnStateChanged(int newState) => CoreAudio.SOk;

    public int OnSessionDisconnected(int disconnectReason)
    {
        onDisconnected(disconnectReason);
        return CoreAudio.SOk;
    }
}
