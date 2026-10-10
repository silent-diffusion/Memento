using System.Drawing;
using System.Windows.Forms;
using Memento.Audio;
using Memento.Audio.Capture;
using Memento.Video.Encoding;
using Memento.Video.Gpu;
using Memento.Video.Recording;
using Memento.Video.Sources;
using NAudio.CoreAudioApi;
using Vortice.MediaFoundation;

namespace Memento.Tools.VideoCheck;

/// <summary>
/// sync --minutes 22 [--period 2] [--adapter amd] [--display 3] [--no-mic] [--out artifacts/video/sync.mp4]
///
/// Video against the audio timeline over a long run (ENGINE-NOTES.md §M.4), without playing any sound. Every
/// <c>period</c> seconds, at one scheduled QPC instant, a small window flashes white. The window is recorded the way
/// Memento records a window (GraphicsCaptureSource → FramePacer → Mp4TrackWriter, frames stamped on the QPC clock).
/// The audio side is a generated WAV on the recording timeline with a click at each scheduled instant (what an audio
/// track holds when its clock is exact); the flashes found in the decoded MP4 are timed against those clicks. Separately,
/// the default microphone is opened and its frames are only counted (nothing is kept or played) to measure how far a
/// real audio device's clock drifts from QPC over the same run, which is what an audio track's file time inherits.
/// </summary>
internal static class SyncCheck
{
    public static int Run(Options o) => RunAsync(o).GetAwaiter().GetResult();

    private static async Task<int> RunAsync(Options o)
    {
        var minutes = o.Double("minutes", 22);
        var period = o.Double("period", 2);
        var fps = o.Int("fps", 30);
        var outPath = Path.GetFullPath(o.Get("out", "artifacts/video/sync.mp4"));
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);

        var display = VideoSourceEnumerator.ListDisplays()[o.Int("display", 1) - 1];
        var monitor = Memento.Video.Interop.User32.Monitors().First(m => m.DeviceName == display.Id.Key);
        using var flash = new FlashWindow(new Rectangle(monitor.Bounds.Left + 40, monitor.Bounds.Top + 40, 320, 240));

        using var device = VideoDevice.Create(Checks.AdapterLuid(o));
        var origin = QpcClock.Now;
        await using var source = GraphicsCaptureSource.ForWindow(device, VideoSourceId.Window(flash.Handle), "Sync flash");
        await source.StartAsync(CancellationToken.None);

        // A real audio device's clock against QPC: microphone frames counted, never stored.
        var micClock = o.Flag("no-mic") ? null : await MicrophoneClock.OpenAsync();

        var settings = new VideoEncoderSettings(320, 240, fps, VideoQuality.Balanced);
        await using var writer = Mp4TrackWriter.Create(device, outPath, settings);
        Console.WriteLine($"Encoder {writer.Encoder.Name}; {minutes} min, a flash every {period} s; microphone clock {(micClock is null ? "off" : "counted")}");
        var pacer = new FramePacer(origin, fps);
        using var stop = new ManualResetEvent(false);
        var videoThread = new Thread(() =>
        {
            using var waiter = new HighResolutionWaiter();
            while (!stop.WaitOne(0))
            {
                waiter.WaitUntil(pacer.NextDueQpc, stop);
                pacer.Tick(QpcClock.Now, source, writer, paused: false);
            }
        })
        { Priority = ThreadPriority.AboveNormal, IsBackground = true };
        videoThread.Start();

        var events = (int)(((minutes * 60) - 4) / period);
        var scheduled = new List<double>();
        var micSamples = new List<(double At, double DriftMs)>();
        using (var waiter = new HighResolutionWaiter())
        using (var never = new ManualResetEvent(false))
        {
            for (var k = 0; k < events; k++)
            {
                var due = origin + (long)((3 + (k * period)) * QpcClock.TicksPerSecond);
                waiter.WaitUntil(due, never);
                flash.Flash();
                scheduled.Add((due - origin) / (double)QpcClock.TicksPerSecond);
                if (micClock?.DriftMs() is { } drift)
                {
                    micSamples.Add(((due - origin) / (double)QpcClock.TicksPerSecond, drift));
                }

                if (k % 60 == 0)
                {
                    Console.WriteLine($"  {k * period / 60:0} min");
                }
            }
        }

        await Task.Delay(1500);
        stop.Set();
        videoThread.Join();
        await writer.FinalizeAsync(CancellationToken.None);
        await source.StopAsync();
        if (micClock is not null)
        {
            await micClock.DisposeAsync();
        }

        var first = (writer.FirstTimestampTicks ?? 0) / 1e7;
        Console.WriteLine($"Video: written {pacer.FramesWritten}, dropped {pacer.FramesDropped}; first frame at {first:0.000} s");

        // The audio track: a generated WAV on the recording timeline with a click at every scheduled instant.
        var wavPath = Path.ChangeExtension(outPath, ".clicks.wav");
        SyntheticClicks.Write(wavPath, scheduled, 48_000);
        var clicks = SyntheticClicks.Find(wavPath);
        var flashes = FlashTimes(outPath, first);
        Console.WriteLine($"Flashes seen in the file: {flashes.Count} of {events}; clicks in the WAV: {clicks.Count}");
        Report(scheduled, flashes, clicks, period, micSamples);
        return 0;
    }

    /// <summary>Decodes the MP4 and returns the times (on the recording timeline) where the picture turns bright.</summary>
    private static List<double> FlashTimes(string path, double firstFrameSeconds)
    {
        using var attributes = MediaFactory.MFCreateAttributes(1);
        attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, 1u);
        using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, attributes);
        using (var type = MediaFactory.MFCreateMediaType())
        {
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
            reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, type);
        }

        var times = new List<double>();
        var bright = false;
        while (true)
        {
            using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out var time);
            if ((flags & SourceReaderFlag.EndOfStream) != 0 || sample is null)
            {
                break;
            }

            using var buffer = sample.ConvertToContiguousBuffer();
            buffer.Lock(out var pointer, out _, out var length);
            var now = MeanGreen(pointer, length) > 128;
            buffer.Unlock();
            if (now && !bright)
            {
                times.Add(firstFrameSeconds + (time / 1e7));
            }

            bright = now;
        }

        return times;
    }

    private static unsafe double MeanGreen(IntPtr pointer, int length)
    {
        var bytes = (byte*)pointer;
        double sum = 0;
        var count = 0;
        for (var i = 0; i + 1 < length; i += 64)
        {
            sum += bytes[i + 1];
            count++;
        }

        return sum / Math.Max(1, count);
    }

    private static void Report(List<double> scheduled, List<double> flashes, List<double> clicks, double period, List<(double At, double DriftMs)> mic)
    {
        var rows = new List<(double At, double Video, double Audio)>();
        foreach (var at in scheduled)
        {
            var v = flashes.Where(f => Math.Abs(f - at) < period / 2).DefaultIfEmpty(double.NaN).First();
            var a = clicks.Where(c => Math.Abs(c - at) < period / 2).DefaultIfEmpty(double.NaN).First();
            if (!double.IsNaN(v) && !double.IsNaN(a))
            {
                rows.Add((at, v, a));
            }
        }

        if (rows.Count < 3)
        {
            Console.WriteLine($"Too few matched events ({rows.Count}).");
            return;
        }

        Console.WriteLine($"Matched {rows.Count} events over {rows[^1].At - rows[0].At:0} s");
        Line("video flash − audio click (file times)", rows.Select(r => (r.At, (r.Video - r.Audio) * 1000)).ToList());
        Line("video flash − schedule", rows.Select(r => (r.At, (r.Video - r.At) * 1000)).ToList());
        if (mic.Count > 3)
        {
            Line("microphone frames − QPC (a real audio clock)", mic.Select(m => (m.At, m.DriftMs)).ToList());
        }
    }

    private static void Line(string name, List<(double X, double Y)> points)
    {
        var mx = points.Average(p => p.X);
        var my = points.Average(p => p.Y);
        var sd = Math.Sqrt(points.Average(p => (p.Y - my) * (p.Y - my)));
        var slope = points.Sum(p => (p.X - mx) * (p.Y - my)) / points.Sum(p => (p.X - mx) * (p.X - mx));
        var head = points.Where(p => p.X < points[0].X + 300).Average(p => p.Y);
        var tail = points.Where(p => p.X > points[^1].X - 300).Average(p => p.Y);
        Console.WriteLine($"{name}: mean {my:0.0} ms, sd {sd:0.0} ms, min {points.Min(p => p.Y):0.0} max {points.Max(p => p.Y):0.0}; slope {slope * 1000:0.00} ms per 1000 s ({slope * 1000:0.0} ppm); first 5 min {head:0.0} ms, last 5 min {tail:0.0} ms");
    }

    /// <summary>The default microphone's frames counted against QPC; samples are never stored.</summary>
    private sealed class MicrophoneClock : IAsyncDisposable
    {
        private readonly WasapiAudioCapture _capture;
        private readonly Task _reader;
        private long _frames;
        private long _firstQpc;
        private long _lastEndQpc;

        private MicrophoneClock(WasapiAudioCapture capture)
        {
            _capture = capture;
            var rate = capture.Format.SampleRate;
            _reader = Task.Run(async () =>
            {
                await foreach (var packet in capture.Packets.ReadAllAsync())
                {
                    if (_firstQpc == 0 && packet.HasReliableTimestamp)
                    {
                        _firstQpc = packet.QpcPosition;
                        Interlocked.Exchange(ref _frames, 0);
                    }

                    if (_firstQpc != 0)
                    {
                        Interlocked.Add(ref _frames, packet.Frames + packet.DroppedFramesBefore);
                        Interlocked.Exchange(ref _lastEndQpc, packet.QpcPosition + QpcClock.FramesToTicks(packet.Frames, rate));
                    }

                    packet.Release();
                }
            });
        }

        public static async Task<MicrophoneClock?> OpenAsync()
        {
            try
            {
                using var enumerator = new MMDeviceEnumerator();
                if (!enumerator.HasDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Console))
                {
                    return null;
                }

                using var endpoint = enumerator.GetDefaultAudioEndpoint(NAudio.CoreAudioApi.DataFlow.Capture, NAudio.CoreAudioApi.Role.Console);
                var capture = await WasapiAudioCapture.OpenAsync(AudioSourceId.Microphone(endpoint.ID));
                capture.Start();
                return new MicrophoneClock(capture);
            }
            catch (AudioSourceUnavailableException)
            {
                return null;
            }
        }

        /// <summary>(Frames counted as time) − (QPC span of the same packets), in ms; grows when the device clock is fast.</summary>
        public double? DriftMs()
        {
            var last = Interlocked.Read(ref _lastEndQpc);
            if (_firstQpc == 0 || last == 0)
            {
                return null;
            }

            var framesSeconds = Interlocked.Read(ref _frames) / (double)_capture.Format.SampleRate;
            return (framesSeconds - ((last - _firstQpc) / (double)QpcClock.TicksPerSecond)) * 1000;
        }

        public async ValueTask DisposeAsync()
        {
            await _capture.StopAsync();
            await _reader;
            await _capture.DisposeAsync();
        }
    }

    /// <summary>A small black window that turns white for 100 ms on <see cref="Flash"/>.</summary>
    private sealed class FlashWindow : IDisposable
    {
        private readonly Thread _thread;
        private Form? _form;

        public FlashWindow(Rectangle bounds)
        {
            using var ready = new ManualResetEventSlim();
            _thread = new Thread(() =>
            {
                _form = new Form
                {
                    FormBorderStyle = FormBorderStyle.None,
                    StartPosition = FormStartPosition.Manual,
                    Bounds = bounds,
                    TopMost = true,
                    BackColor = Color.Black,
                    ShowInTaskbar = false,
                    Text = "Sync flash",
                };
                _form.Shown += (_, _) => ready.Set();
                Application.Run(_form);
            });
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            ready.Wait(5000);
            Handle = (IntPtr)_form!.Invoke(() => _form.Handle);
        }

        public IntPtr Handle { get; }

        public void Flash()
        {
            _form?.BeginInvoke(async () =>
            {
                _form.BackColor = Color.White;
                _form.Refresh();
                await Task.Delay(100);
                _form.BackColor = Color.Black;
                _form.Refresh();
            });
        }

        public void Dispose()
        {
            _form?.BeginInvoke(() => _form.Close());
            _thread.Join(2000);
        }
    }
}
