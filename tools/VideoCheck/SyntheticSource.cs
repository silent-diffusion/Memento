using Memento.Audio;
using Memento.Video;
using Memento.Video.Gpu;
using Memento.Video.Sources;
using Vortice;
using Vortice.Direct3D11;
using Vortice.DXGI;
using Vortice.Mathematics;

namespace Memento.Tools.VideoCheck;

/// <summary>
/// A test picture drawn on the GPU at the moment it is asked for: a slowly changing background, a moving block, and the
/// frame's slot number as a 24-bit barcode (white = 1) in the top-left corner, 32 px squares, so a decoder or the
/// browser can read which slot a displayed picture belongs to.
/// </summary>
internal sealed class SyntheticSource : IVideoSource
{
    public const int BarcodeBits = 24;
    public const int BarcodeCell = 32;
    private readonly VideoDevice _device;
    private readonly int _width;
    private readonly int _height;
    private readonly long _originQpc;

    /// <summary>Slot 0's time on the clock.</summary>
    public long OriginQpc => _originQpc;
    private readonly long _period;
    private ID3D11Texture2D? _texture;
    private ID3D11RenderTargetView? _view;
    private ID3D11DeviceContext1? _context1;
    private long _frames;

    /// <summary>Hundreds of random blocks per frame: the encoder's worst case, to see each quality's bitrate ceiling.</summary>
    public bool Busy { get; init; }

    /// <summary>A virtual clock for encoding faster than real time (--fast); null uses the real one.</summary>
    public Func<long>? Clock { get; set; }

    public SyntheticSource(VideoDevice device, int width, int height, long originQpc, int frameRate)
    {
        _device = device;
        _width = width;
        _height = height;
        _originQpc = originQpc;
        _period = 10_000_000L / frameRate;
    }

    public VideoSourceId Id => VideoSourceId.Display("synthetic");

    public string Name => "Synthetic";

    public VideoSourceState State { get; private set; } = VideoSourceState.Created;

    public VideoSourceStatistics Statistics => new(_frames, _originQpc, QpcClock.Now, LatencyStatistics.Empty, _width, _height);

    public VideoCaptureException? Loss => null;

#pragma warning disable CS0067 // Never raised by a test picture.
    public event EventHandler<VideoSourceLostEventArgs>? Lost;

    public event EventHandler<VideoSourceStateChangedEventArgs>? StateChanged;
#pragma warning restore CS0067

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _texture = _device.Device.CreateTexture2D(new Texture2DDescription
        {
            Width = (uint)_width,
            Height = (uint)_height,
            MipLevels = 1,
            ArraySize = 1,
            Format = Format.B8G8R8A8_UNorm,
            SampleDescription = new SampleDescription(1, 0),
            Usage = ResourceUsage.Default,
            BindFlags = BindFlags.ShaderResource | BindFlags.RenderTarget,
        });
        _view = _device.Device.CreateRenderTargetView(_texture);
        _context1 = _device.Context.QueryInterface<ID3D11DeviceContext1>();
        State = VideoSourceState.Running;
        return Task.CompletedTask;
    }

    public bool TryGetLatest(out VideoFrame frame)
    {
        var now = Clock?.Invoke() ?? QpcClock.Now;
        var slot = (now - _originQpc) / _period;
        Draw(slot);
        _frames++;
        frame = new VideoFrame(_texture, 0, _width, _height, _originQpc + (slot * _period), _frames);
        return true;
    }

    private void Draw(long slot)
    {
        var shade = (float)(0.25 + (0.2 * Math.Sin(slot / 40.0)));
        _context1!.ClearView(_view!, new Color4(shade, 0.3f, 0.45f, 1f), []);
        var x = (int)(slot * 8 % Math.Max(1, _width - 200));
        _context1.ClearView(_view!, new Color4(0.9f, 0.6f, 0.1f, 1f), [new RawRect(x, _height / 2, x + 200, (_height / 2) + 120)]);
        if (Busy)
        {
            var random = new Random((int)slot);
            for (var c = 0; c < 8; c++)
            {
                var rects = new RawRect[40];
                for (var r = 0; r < rects.Length; r++)
                {
                    var rx = random.Next(_width - 64);
                    var ry = random.Next(BarcodeCell, _height - 64);
                    rects[r] = new RawRect(rx, ry, rx + random.Next(8, 64), ry + random.Next(8, 64));
                }

                _context1.ClearView(_view!, new Color4((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble(), 1f), rects);
            }
        }

        var ones = new List<RawRect>();
        var zeros = new List<RawRect>();
        for (var bit = 0; bit < BarcodeBits; bit++)
        {
            var rect = new RawRect(bit * BarcodeCell, 0, (bit + 1) * BarcodeCell, BarcodeCell);
            ((slot >> bit & 1) == 1 ? ones : zeros).Add(rect);
        }

        if (ones.Count > 0)
        {
            _context1.ClearView(_view!, new Color4(1f, 1f, 1f, 1f), [.. ones]);
        }

        if (zeros.Count > 0)
        {
            _context1.ClearView(_view!, new Color4(0f, 0f, 0f, 1f), [.. zeros]);
        }
    }

    public Task StopAsync()
    {
        State = VideoSourceState.Stopped;
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _context1?.Dispose();
        _view?.Dispose();
        _texture?.Dispose();
        return ValueTask.CompletedTask;
    }
}
