using System.Diagnostics;
using Memento.Audio.Capture;

namespace Memento.Audio.Tests.Capture;

/// <summary>Short real-device checks. Microphone samples are only counted and metered, never written.</summary>
public sealed class WasapiAudioCaptureHardwareTests
{
    [Trait("Category", "Hardware")]
    [HardwareFact(needsMicrophone: true)]
    public async Task MicrophoneDeliversTimestampedPacketsAtTheMixFormat()
    {
        await using var capture = await WasapiAudioCapture.OpenAsync(AudioSourceId.Microphone(Hardware.DefaultMicrophoneId!));
        var result = await RunFor(capture, TimeSpan.FromSeconds(1.5));

        Assert.True(capture.Format.SampleRate >= 8_000);
        Assert.True(result.Packets > 20, $"only {result.Packets} packets");
        Assert.True(result.MonotonicTimestamps);
        Assert.InRange(result.FrameSeconds, 1.0, 1.6);
        Assert.Null(capture.Loss);
    }

    [Trait("Category", "Hardware")]
    [HardwareFact(needsRender: true)]
    public async Task SystemLoopbackKeepsTheTrackOnTheClockEvenInSilence()
    {
        await using var capture = await WasapiAudioCapture.OpenAsync(AudioSourceId.System(Hardware.DefaultRenderId!));
        var result = await RunFor(capture, TimeSpan.FromSeconds(1.5));

        // Real packets (if something plays) or clock-timed silence (if nothing does) cover the run minus the holdback.
        Assert.InRange(result.FrameSeconds, 1.25, 1.6);
        Assert.True(result.MonotonicTimestamps);
    }

    [Trait("Category", "Hardware")]
    [HardwareFact(needsRender: true)]
    public async Task ProcessLoopbackOfThisProcessTreeStreamsFloat48kStereo()
    {
        await using var capture = await WasapiAudioCapture.OpenAsync(AudioSourceId.Application(Environment.ProcessId));
        var result = await RunFor(capture, TimeSpan.FromSeconds(1.5));

        Assert.Equal(AudioFormat.Float32Stereo48k, capture.Format);
        Assert.InRange(result.FrameSeconds, 1.25, 1.6);
        Assert.Null(capture.Loss);
    }

    [Fact]
    public async Task AMissingProcessIsReportedAsUnavailableByName()
    {
        var pid = UnusedPid();
        var ex = await Assert.ThrowsAsync<AudioSourceUnavailableException>(() => WasapiAudioCapture.OpenAsync(AudioSourceId.Application(pid)));

        Assert.Contains("no longer running", ex.Message, StringComparison.Ordinal);
        Assert.Equal(AudioSourceId.Application(pid), ex.SourceId);
    }

    [Fact]
    public async Task AnUnknownEndpointIsReportedAsUnavailable()
    {
        var ex = await Assert.ThrowsAsync<AudioSourceUnavailableException>(
            () => WasapiAudioCapture.OpenAsync(AudioSourceId.Microphone("{0.0.1.00000000}.{00000000-0000-0000-0000-000000000000}")));

        Assert.Contains("microphone", ex.Message, StringComparison.Ordinal);
    }

    private static int UnusedPid()
    {
        var used = Process.GetProcesses().Select(p => p.Id).ToHashSet();
        var pid = 4_000_000;
        while (used.Contains(pid))
        {
            pid += 4;
        }

        return pid;
    }

    private static async Task<RunResult> RunFor(WasapiAudioCapture capture, TimeSpan duration)
    {
        long frames = 0;
        var packets = 0;
        var monotonic = true;
        long lastEnd = 0;
        var rate = capture.Format.SampleRate;
        var reader = Task.Run(async () =>
        {
            await foreach (var packet in capture.Packets.ReadAllAsync())
            {
                packets++;
                frames += packet.Frames + packet.DroppedFramesBefore;
                if (packet.QpcPosition + 20_000 < lastEnd)
                {
                    monotonic = false;
                }

                lastEnd = packet.QpcPosition + QpcClock.FramesToTicks(packet.Frames, rate);
                packet.Release();
            }
        });

        capture.Start();
        await Task.Delay(duration);
        await capture.StopAsync();
        await reader;
        return new RunResult(packets, frames / (double)rate, monotonic);
    }

    private sealed record RunResult(int Packets, double FrameSeconds, bool MonotonicTimestamps);
}
