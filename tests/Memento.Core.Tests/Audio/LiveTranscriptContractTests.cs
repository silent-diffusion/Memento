using System.Buffers.Binary;
using System.Text.Json;
using Memento.Core.Audio;
using Memento.Core.Bridge;
using Memento.Core.Bridge.Contracts;
using Memento.Core.Tests.Fakes;
using Memento.Core.Workers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Memento.Core.Tests.Audio;

/// <summary>
/// The live transcript's pieces in Core (2.0): the windowing, the mix read behind the writers, the worker protocol
/// lines, the bridge event and the setting, and the graphics-card rules it relies on in <see cref="WorkerClient"/>.
/// </summary>
public sealed class LiveTranscriptContractTests : IDisposable
{
    /// <summary>The middle dot as System.Text.Json escapes it.</summary>
    private const string Dot = "\\" + "u00B7";

    private readonly TempDirectory _directory = new();

    public void Dispose() => _directory.Dispose();

    [Fact]
    public void WindowsAreHeardInOrderOnlyOnceEveryOpenTrackCoversThem()
    {
        Assert.Null(LiveWindows.Next(0, 9_999));
        Assert.Equal(new LiveWindow(0, 0, 10_000, 0), LiveWindows.Next(0, 10_000));
        Assert.Equal(new LiveWindow(1, 10_000, 20_000, 0), LiveWindows.Next(1, 21_000));
        Assert.Null(LiveWindows.Next(2, 21_000));
    }

    [Fact]
    public void ADraftThatFellBehindSkipsToTheLatestCompleteWindow()
    {
        Assert.Equal(new LiveWindow(4, 40_000, 50_000, 3), LiveWindows.Next(1, 52_000));
        Assert.Equal(new LiveWindow(1, 10_000, 20_000, 0), LiveWindows.Next(1, 29_999));
    }

    [Fact]
    public void CoverageIsTheSlowestOpenTrackNeverPastTheRecordedTime()
    {
        Assert.Equal(12_000, LiveWindows.Covered([(15_000, true), (12_000, true), (3_000, false)], 30_000));
        Assert.Equal(14_000, LiveWindows.Covered([(15_000, true)], 14_000));
        Assert.Equal(0, LiveWindows.Covered([(5_000, false)], 14_000));
    }

    [Fact]
    public void TheMixPlacesEachTrackByItsOffsetResamplesTo16KilohertzAndNeverClips()
    {
        // A 48 kHz stereo 24-bit track from 0 s and a 44.1 kHz mono 16-bit one turned on at 2 s, both full scale.
        WriteWav("tracks/mic.wav", new PcmFormat(48_000, 2, 24, SampleEncoding.Pcm), seconds: 12, value: 0.9);
        WriteWav("tracks/app-zoom.wav", PcmFormat.Pcm16(44_100, 1), seconds: 10, value: 0.9);
        var tracks = new List<LiveTrackSource>
        {
            new("mic", _directory.Path, "tracks/mic.wav", 0, null),
            new("app-zoom", _directory.Path, "tracks/app-zoom.wav", 2_000, null),
        };

        var mix = LiveMixReader.Read(tracks, 0, 10_000);

        Assert.Equal(160_000, mix.Samples.Length);
        Assert.Equal(2, mix.Tracks);
        Assert.InRange(mix.Samples[16_000], 0.40, 0.55); // 1 s: the microphone alone, scaled with the rest
        Assert.InRange(mix.Samples[80_000], 0.95, 1.0); // 5 s: both, scaled down so the sum does not clip
        Assert.All(mix.Samples, s => Assert.InRange(s, -1f, 1f));
        Assert.False(mix.IsSilent);
        Assert.Equal(12_000, LiveMixReader.CoveredUntilMs(tracks[0]));
        Assert.Equal(12_000, LiveMixReader.CoveredUntilMs(tracks[1]));
    }

    [Fact]
    public void TheMixIsReadBehindAWriterThatStillHasTheFileOpen()
    {
        var format = PcmFormat.Pcm16(48_000, 1);
        var path = Path.Combine(_directory.Path, "tracks", "mic.wav");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StreamingWavWriter(path, format);
        var source = new LiveTrackSource("mic", _directory.Path, "tracks/mic.wav", 0, null);
        writer.Write(Samples(format, 4, 0.5));
        writer.Checkpoint();
        Assert.Equal(4_000, LiveMixReader.CoveredUntilMs(source));

        writer.Write(Samples(format, 8, 0.5));
        writer.Checkpoint();

        Assert.Equal(12_000, LiveMixReader.CoveredUntilMs(source));
        var mix = LiveMixReader.Read([source], 0, 10_000);
        Assert.InRange(mix.Samples[100_000], 0.45, 0.55);
    }

    [Fact]
    public void SilenceAndATrackThatEndedEarlyAddNothing()
    {
        WriteWav("tracks/mic.wav", PcmFormat.Pcm16(16_000, 1), seconds: 10, value: 0);
        WriteWav("tracks/system.wav", PcmFormat.Pcm16(16_000, 1), seconds: 3, value: 0.5);

        var mix = LiveMixReader.Read(
            [new("mic", _directory.Path, "tracks/mic.wav", 0, null), new("system", _directory.Path, "tracks/system.wav", 0, 0)],
            0,
            10_000);

        Assert.True(mix.IsSilent);
        Assert.Equal(1, mix.Tracks);
    }

    [Fact]
    public void ResamplingKeepsTheLevel()
    {
        var input = Enumerable.Repeat(0.25f, 48_000).ToArray();

        var output = LiveMixReader.Resample(input, 48_000, 16_000);

        Assert.Equal(16_000, output.Length);
        Assert.All(output, s => Assert.Equal(0.25f, s, 0.0001f));
    }

    [Fact]
    public void TheWorkerLinesAreStrictJson()
    {
        var audio = new LiveAudio(3, 30.0, LiveAudio.Encode([0f, 0.5f, -1f, 2f]));
        Assert.Equal(
            """{"type":"audio","audio":{"window":3,"startSeconds":30,"pcm16":"AAAAQAGA/38="}}""",
            JsonSerializer.Serialize(new WorkerCommand(WorkerMessageTypes.Audio, Audio: audio), WorkerJsonContext.Default.WorkerCommand));
        Assert.Equal([0f, 0.5f, -1f, 1f], audio.Decode().Select(s => MathF.Round(s, 3)));

        var job = new WorkerJob(WorkerJobKinds.Live, Live: new LiveJob("m.bin", "whisper-small", [WorkerRuntimes.Cpu], -1, null, "auto", "p", 2));
        Assert.Equal(
            """{"kind":"live","live":{"modelPath":"m.bin","modelId":"whisper-small","runtimes":["cpu"],"gpuDevice":-1,"language":"auto","prompt":"p","threads":2}}""",
            JsonSerializer.Serialize(job, WorkerJsonContext.Default.WorkerJob).Replace("\"transcribe\":null,", string.Empty, StringComparison.Ordinal));
        Assert.False(job.UsesGpu);
        Assert.True((job with { Live = job.Live! with { Runtimes = [WorkerRuntimes.Vulkan, WorkerRuntimes.Cpu] } }).UsesGpu);

        var heard = JsonSerializer.Deserialize("""{"type":"heard","window":3,"segments":[{"start":31,"end":33,"text":"Hello.","confidence":0.9,"words":[]}]}""", WorkerJsonContext.Default.WorkerReply)!;
        Assert.Equal(WorkerMessageTypes.Heard, heard.Type);
        Assert.Equal(3, heard.Window);
        Assert.Equal("Hello.", Assert.Single(heard.Segments!).Text);
    }

    [Fact]
    public void TheBridgeEventIsStrictJson()
    {
        Assert.Equal(
            """{"event":"recording.liveTranscript","payload":{"sessionId":"s1","segments":[{"start":1.5,"end":3,"text":"Rough draft."}],"state":"paused","engine":"Local """ + Dot + " CPU " + Dot + """ Small","note":"Paused with the recording."}}""",
            BridgeEventPublisher.Serialize(
                BridgeEventNames.RecordingLiveTranscript,
                new LiveTranscriptPayload("s1", [new LiveTranscriptSegment(1.5, 3, "Rough draft.")], LiveTranscriptStates.Paused, "Local · CPU · Small", "Paused with the recording."),
                LiveBridgeJsonContext.Default.BridgeEventEnvelopeLiveTranscriptPayload));
    }

    [Fact]
    public async Task TheSettingIsOffByDefaultAndTheCardMustBeAllowedSeparately()
    {
        using var host = new BridgeTestHost();
        var before = (await host.ResultAsync("settings.get")).GetProperty("transcription");
        Assert.Equal("after", before.GetProperty("timing").GetString());
        Assert.False(before.GetProperty("liveOnGpu").GetBoolean());

        var after = (await host.ResultAsync("settings.set", """{"transcription":{"timing":"during","liveOnGpu":true}}""")).GetProperty("transcription");

        Assert.Equal("during", after.GetProperty("timing").GetString());
        Assert.True(after.GetProperty("liveOnGpu").GetBoolean());
        Assert.True(host.Settings.Current.Transcription.LiveDuringRecording);
        Assert.True(host.Settings.Current.Transcription.LiveOnGpu);
    }

    [Fact]
    public async Task AJobThatTriesForTheCardNeverQueuesAndOneThatWaitsSaysSo()
    {
        var launcher = new ScriptedWorkerLauncher();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        launcher.Script = async (_, context, cancel) =>
        {
            await release.Task.WaitAsync(cancel);
            context.Send(new WorkerReply { Type = WorkerMessageTypes.Result });
            return 0;
        };
        using var workers = new WorkerClient(launcher, NullLogger<WorkerClient>.Instance);
        var card = new WorkerJob(WorkerJobKinds.Live, Live: new LiveJob("m", "whisper-small", [WorkerRuntimes.Vulkan, WorkerRuntimes.Cpu], -1, null, "auto", "p", 2));
        var wanted = 0;
        workers.GpuWanted += (_, _) => wanted++;

        var first = await workers.TryOpenAsync(card, null, CancellationToken.None);
        Assert.NotNull(first);
        Assert.True(workers.IsGpuBusy);
        Assert.Null(await workers.TryOpenAsync(card, null, CancellationToken.None));
        Assert.Equal(0, wanted);

        var waiting = workers.RunAsync(card, null, CancellationToken.None);
        await TestRecordings.WaitUntilAsync(() => wanted == 1, "the waiting job to ask for the card");
        await first.DisposeAsync();
        release.SetResult();
        await waiting.WaitAsync(Patience.Ceiling);
        Assert.False(workers.IsGpuBusy);
    }

    private void WriteWav(string relative, PcmFormat format, double seconds, double value)
    {
        var path = Path.Combine(_directory.Path, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var writer = new StreamingWavWriter(path, format);
        writer.Write(Samples(format, seconds, value));
        writer.Checkpoint();
    }

    private static byte[] Samples(PcmFormat format, double seconds, double value)
    {
        var frames = (int)(format.SampleRate * seconds);
        var bytesPerSample = format.BitsPerSample / 8;
        var bytes = new byte[frames * format.BlockAlign];
        for (var i = 0; i < frames * format.Channels; i++)
        {
            var o = i * bytesPerSample;
            if (bytesPerSample == 2)
            {
                BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(o), (short)(value * short.MaxValue));
            }
            else
            {
                var v = (int)(value * 8_388_607);
                bytes[o] = (byte)v;
                bytes[o + 1] = (byte)(v >> 8);
                bytes[o + 2] = (byte)(v >> 16);
            }
        }

        return bytes;
    }
}
