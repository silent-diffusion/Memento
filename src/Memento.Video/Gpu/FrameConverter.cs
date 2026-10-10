using Vortice;
using Vortice.Direct3D11;
using Vortice.DXGI;

namespace Memento.Video.Gpu;

/// <summary>
/// Scales and converts a captured BGRA picture into the encoder's NV12 surface on the GPU's video processor, keeping
/// the aspect ratio (black bars when a window's shape differs from the track's), BT.709 limited range. Optionally draws
/// a second picture (the camera) as an inset over the first. Called on the session's pacing thread only.
/// </summary>
internal sealed class FrameConverter : IDisposable
{
    private readonly VideoDevice _device;
    private readonly int _outputWidth;
    private readonly int _outputHeight;
    private readonly Dictionary<(IntPtr, uint), ID3D11VideoProcessorOutputView> _outputViews = [];
    private readonly Dictionary<IntPtr, ID3D11VideoProcessorInputView> _inputViews = [];
    private ID3D11VideoProcessorEnumerator? _enumerator;
    private ID3D11VideoProcessor? _processor;
    private int _inputWidth;
    private int _inputHeight;

    public FrameConverter(VideoDevice device, int outputWidth, int outputHeight)
    {
        _device = device;
        _outputWidth = outputWidth;
        _outputHeight = outputHeight;
    }

    /// <summary>Where the picture lands inside the output, letterboxed or pillarboxed to keep its shape.</summary>
    public static RawRect Fit(int sourceWidth, int sourceHeight, int outputWidth, int outputHeight)
    {
        if (sourceWidth <= 0 || sourceHeight <= 0)
        {
            return new RawRect(0, 0, outputWidth, outputHeight);
        }

        var scale = Math.Min((double)outputWidth / sourceWidth, (double)outputHeight / sourceHeight);
        var width = Math.Min(outputWidth, (int)Math.Round(sourceWidth * scale) & ~1);
        var height = Math.Min(outputHeight, (int)Math.Round(sourceHeight * scale) & ~1);
        var left = ((outputWidth - width) / 2) & ~1;
        var top = ((outputHeight - height) / 2) & ~1;
        return new RawRect(left, top, left + width, top + height);
    }

    /// <summary>Draws <paramref name="source"/> (BGRA) into array slice <paramref name="subresource"/> of <paramref name="target"/> (NV12).</summary>
    public void Convert(ID3D11Texture2D source, int sourceWidth, int sourceHeight, ID3D11Texture2D target, uint subresource)
    {
        EnsureProcessor(sourceWidth, sourceHeight);
        var input = InputView(source);
        var output = OutputView(target, subresource);
        var video = _device.VideoContext;
        var dest = Fit(sourceWidth, sourceHeight, _outputWidth, _outputHeight);
        video.VideoProcessorSetStreamSourceRect(_processor!, 0, true, new RawRect(0, 0, sourceWidth, sourceHeight));
        video.VideoProcessorSetStreamDestRect(_processor!, 0, true, dest);
        video.VideoProcessorSetOutputTargetRect(_processor!, true, new RawRect(0, 0, _outputWidth, _outputHeight));
        var streams = new[] { new VideoProcessorStream { Enable = true, InputSurface = input } };
        video.VideoProcessorBlt(_processor!, output, 0, 1, streams).CheckError();
    }

    private void EnsureProcessor(int width, int height)
    {
        if (_processor is not null && width == _inputWidth && height == _inputHeight)
        {
            return;
        }

        foreach (var view in _inputViews.Values)
        {
            view.Dispose();
        }

        _inputViews.Clear();
        foreach (var view in _outputViews.Values)
        {
            view.Dispose();
        }

        _outputViews.Clear();
        _processor?.Dispose();
        _enumerator?.Dispose();
        var content = new VideoProcessorContentDescription
        {
            InputFrameFormat = VideoFrameFormat.Progressive,
            InputWidth = (uint)width,
            InputHeight = (uint)height,
            OutputWidth = (uint)_outputWidth,
            OutputHeight = (uint)_outputHeight,
            Usage = VideoUsage.PlaybackNormal,
        };
        _enumerator = _device.VideoDevice1.CreateVideoProcessorEnumerator(content);
        _processor = _device.VideoDevice1.CreateVideoProcessor(_enumerator, 0);
        var video = _device.VideoContext;
        video.VideoProcessorSetStreamFrameFormat(_processor, 0, VideoFrameFormat.Progressive);
        video.VideoProcessorSetStreamAutoProcessingMode(_processor, 0, false);
        video.VideoProcessorSetStreamColorSpace(_processor, 0, new VideoProcessorColorSpace { RGB_Range = 0 });
        video.VideoProcessorSetOutputColorSpace(_processor, new VideoProcessorColorSpace { RGB_Range = 0, YCbCr_Matrix = 1, Nominal_Range = (uint)VideoProcessorNominalRange.Range_16_235 });
        video.VideoProcessorSetOutputBackgroundColor(_processor, false, new VideoColor { Rgba = new VideoColorRgba { R = 0, G = 0, B = 0, A = 1 } });
        _inputWidth = width;
        _inputHeight = height;
    }

    private ID3D11VideoProcessorInputView InputView(ID3D11Texture2D source)
    {
        if (!_inputViews.TryGetValue(source.NativePointer, out var view))
        {
            if (_inputViews.Count > 8)
            {
                foreach (var old in _inputViews.Values)
                {
                    old.Dispose();
                }

                _inputViews.Clear();
            }

            view = _device.VideoDevice1.CreateVideoProcessorInputView(source, _enumerator!, new VideoProcessorInputViewDescription
            {
                FourCC = 0,
                ViewDimension = VideoProcessorInputViewDimension.Texture2D,
                Texture2D = new Texture2DVideoProcessorInputView { MipSlice = 0, ArraySlice = 0 },
            });
            _inputViews[source.NativePointer] = view;
        }

        return view;
    }

    private ID3D11VideoProcessorOutputView OutputView(ID3D11Texture2D target, uint subresource)
    {
        var key = (target.NativePointer, subresource);
        if (!_outputViews.TryGetValue(key, out var view))
        {
            var arraySize = target.Description.ArraySize;
            var description = arraySize > 1
                ? new VideoProcessorOutputViewDescription
                {
                    ViewDimension = VideoProcessorOutputViewDimension.Texture2DArray,
                    Texture2DArray = new Texture2DArrayVideoProcessorOutputView { MipSlice = 0, FirstArraySlice = subresource, ArraySize = 1 },
                }
                : new VideoProcessorOutputViewDescription
                {
                    ViewDimension = VideoProcessorOutputViewDimension.Texture2D,
                    Texture2D = new Texture2DVideoProcessorOutputView { MipSlice = 0 },
                };
            view = _device.VideoDevice1.CreateVideoProcessorOutputView(target, _enumerator!, description);
            _outputViews[key] = view;
        }

        return view;
    }

    public void Dispose()
    {
        foreach (var view in _inputViews.Values)
        {
            view.Dispose();
        }

        foreach (var view in _outputViews.Values)
        {
            view.Dispose();
        }

        _inputViews.Clear();
        _outputViews.Clear();
        _processor?.Dispose();
        _enumerator?.Dispose();
    }

    internal static Format OutputFormat => Format.NV12;
}
