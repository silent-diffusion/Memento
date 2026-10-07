using System.Runtime.InteropServices;
using Memento.Audio.Capture;
using Memento.Audio.Capture.Interop;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Audio.Tests.Capture;

/// <summary>The capture loop's drain against a scripted capture client (no audio device, no capture thread).</summary>
public sealed class WasapiDrainTests
{
    private static readonly AudioFormat Stereo16 = new(48_000, 2, 16, AudioSampleEncoding.Pcm);

    [Fact]
    public void APacketThatCannotBeReleasedIsKeptAndEndsTheCaptureAsLost()
    {
        using var client = new ScriptedCaptureClient(packets: 3, frames: 4, blockAlign: Stereo16.BlockAlign, releaseResult: CoreAudio.AudclntEDeviceInvalidated);
        var capture = new WasapiAudioCapture(AudioSourceId.Microphone("test-endpoint"), CaptureOptions.Default, NullLogger.Instance);

        var hr = capture.Drain(client, Stereo16, filler: null);

        Assert.Equal(CoreAudio.AudclntEDeviceInvalidated, hr);
        Assert.Equal(1, client.BuffersTaken);
        Assert.True(capture.Packets.TryRead(out var packet));
        Assert.Equal(4, packet.Frames);
        packet.Release();
        Assert.False(capture.Packets.TryRead(out _));
    }

    [Fact]
    public void ReleasedPacketsAreAllDrained()
    {
        using var client = new ScriptedCaptureClient(packets: 3, frames: 4, blockAlign: Stereo16.BlockAlign, releaseResult: CoreAudio.SOk);
        var capture = new WasapiAudioCapture(AudioSourceId.Microphone("test-endpoint"), CaptureOptions.Default, NullLogger.Instance);

        var hr = capture.Drain(client, Stereo16, filler: null);

        Assert.Equal(CoreAudio.SOk, hr);
        Assert.Equal(3, client.BuffersTaken);
        for (var i = 0; i < 3; i++)
        {
            Assert.True(capture.Packets.TryRead(out var packet));
            packet.Release();
        }
    }

    /// <summary>Hands out <c>packets</c> packets of <c>frames</c> frames; ReleaseBuffer answers <c>releaseResult</c>.</summary>
    private sealed class ScriptedCaptureClient(int packets, int frames, int blockAlign, int releaseResult) : CoreAudio.IAudioCaptureClient, IDisposable
    {
        private readonly IntPtr _data = Marshal.AllocHGlobal(frames * blockAlign);

        public int BuffersTaken { get; private set; }

        public int GetBuffer(out IntPtr data, out uint framesToRead, out uint flags, out ulong devicePosition, out ulong qpcPosition)
        {
            data = _data;
            framesToRead = (uint)frames;
            flags = 0;
            devicePosition = (ulong)(BuffersTaken * frames);
            qpcPosition = (ulong)(1_000_000 + (BuffersTaken * 1_000));
            BuffersTaken++;
            return CoreAudio.SOk;
        }

        public int ReleaseBuffer(uint framesRead) => releaseResult;

        public int GetNextPacketSize(out uint framesInNextPacket)
        {
            framesInNextPacket = BuffersTaken < packets ? (uint)frames : 0;
            return CoreAudio.SOk;
        }

        public void Dispose() => Marshal.FreeHGlobal(_data);
    }
}
