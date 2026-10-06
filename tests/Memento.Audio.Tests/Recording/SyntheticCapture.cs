using System.Threading.Channels;
using Memento.Audio.Capture;

namespace Memento.Audio.Tests.Recording;

/// <summary>
/// A real-time synthetic source: after a start-up latency it delivers 10 ms float32 packets stamped on the QPC
/// clock, like WASAPI does, and can simulate losing its device.
/// </summary>
internal sealed class SyntheticCapture : IAudioCapture
{
    private readonly Channel<CapturePacket> _channel = Channel.CreateBounded<CapturePacket>(new BoundedChannelOptions(1024) { SingleReader = true, SingleWriter = true });
    private readonly Thread _thread;
    private readonly ManualResetEventSlim _go = new(false);
    private readonly TaskCompletionSource _exited = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Func<long, float> _signal;
    private readonly TimeSpan _latency;
    private readonly TimeSpan? _loseAfter;
    private volatile bool _stop;
    private long _startedAt;
    private long _frames;
    private long _packets;

    public SyntheticCapture(AudioSourceId source, Func<long, float> signal, TimeSpan? loseAfter = null, TimeSpan? latency = null)
    {
        Source = source;
        _signal = signal;
        _loseAfter = loseAfter;
        _latency = latency ?? TimeSpan.FromMilliseconds(50);
        _thread = new Thread(Run) { IsBackground = true, Name = "synthetic capture" };
        _thread.Start();
    }

    public event EventHandler<CaptureLostEventArgs>? Lost;

    public AudioSourceId Source { get; }

    public AudioFormat Format => AudioFormat.Float32Stereo48k;

    public ChannelReader<CapturePacket> Packets => _channel.Reader;

    public CaptureStatistics Statistics => CaptureStatistics.Empty with { Packets = Interlocked.Read(ref _packets), Frames = Interlocked.Read(ref _frames) };

    public long StartedAtQpc => Interlocked.Read(ref _startedAt);

    public CaptureLostEventArgs? Loss { get; private set; }

    public bool Disposed { get; private set; }

    public void Start() => _go.Set();

    public Task StopAsync()
    {
        _stop = true;
        _go.Set();
        return _exited.Task;
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        while (_channel.Reader.TryRead(out var p))
        {
            p.Release();
        }

        Disposed = true;
    }

    private void Run()
    {
        try
        {
            _go.Wait();
            if (_stop)
            {
                return;
            }

            var start = QpcClock.Now;
            Interlocked.Exchange(ref _startedAt, start);
            var origin = start + _latency.Ticks;
            const int packet = 480;
            var samples = new float[packet * 2];
            long frame = 0;
            while (true)
            {
                var now = QpcClock.Now;
                if (_loseAfter is { } lose && now - start >= lose.Ticks)
                {
                    Loss = new CaptureLostEventArgs(Source, CaptureLostReason.DeviceInvalidated, unchecked((int)0x88890004), now);
                    ThreadPool.QueueUserWorkItem(_ => Lost?.Invoke(this, Loss));
                    return;
                }

                var end = origin + QpcClock.FramesToTicks(frame + packet, 48_000);
                if (now >= end)
                {
                    for (var i = 0; i < packet; i++)
                    {
                        var v = _signal(frame + i);
                        samples[2 * i] = v;
                        samples[(2 * i) + 1] = v;
                    }

                    var bytes = System.Runtime.InteropServices.MemoryMarshal.AsBytes(samples.AsSpan());
                    var p = CapturePacket.FromData(bytes, packet, origin + QpcClock.FramesToTicks(frame, 48_000));
                    if (!_channel.Writer.TryWrite(p))
                    {
                        p.Release();
                    }

                    frame += packet;
                    Interlocked.Add(ref _frames, packet);
                    Interlocked.Increment(ref _packets);
                    continue;
                }

                if (_stop)
                {
                    return;
                }

                Thread.Sleep(1);
            }
        }
        finally
        {
            _channel.Writer.TryComplete();
            _exited.TrySetResult();
        }
    }
}
