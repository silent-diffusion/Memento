using System.Diagnostics;
using System.Runtime.InteropServices;
using Memento.Audio;
using Memento.Video.Gpu;
using Memento.Video.Sources;
using Microsoft.Win32.SafeHandles;
using SharpGen.Runtime;
using Vortice.Direct3D11;
using Vortice.MediaFoundation;

namespace Memento.Video.Encoding;

/// <summary>
/// Writes one H.264 video track as <b>fragmented MP4</b> through the Media Foundation sink writer
/// (<c>MF_TRANSCODE_CONTAINERTYPE = FMPEG4</c>): the header is written first and then one fragment per key frame, so a
/// crash keeps everything up to the last complete fragment and <see cref="Recovery.VideoRecovery"/> can cut a torn
/// tail off. Pictures go to the encoder as NV12 surfaces on the GPU (from a bounded sample pool); a hardware encoder
/// (NVENC, Quick Sync, VCN) on the device's adapter is used when there is one and the settings allow it, otherwise
/// Microsoft's software encoder (ENGINE-NOTES.md §M.3).
/// </summary>
public sealed class Mp4TrackWriter : IVideoTrackWriter
{
    /// <summary>Surfaces in flight between the pacing thread and the encoder; more frames than this behind means drops.</summary>
    internal const int SurfacePoolSize = 8;

    private readonly VideoDevice _device;
    private readonly IMFSinkWriter _writer;
    private readonly IMFVideoSampleAllocatorEx _allocator;
    private readonly FrameConverter _converter;
    private readonly SafeFileHandle? _flushHandle;
    private readonly int _stream;
    private readonly Queue<(long Index, long SubmittedQpc)> _pending = new();
    private long _submitted;
    private long _encoded;
    private double _latencySumMs;
    private long _latencyCount;
    private double _latencyMaxMs;
    private VideoCaptureException? _failure;
    private IMFSample? _held;
    private long _heldTime;
    private long? _firstTimestamp;
    private long _endTimestamp;
    private bool _finalized;
    private bool _disposed;

    private Mp4TrackWriter(VideoDevice device, string path, VideoEncoderSettings settings, H264EncoderInfo encoder, IMFSinkWriter writer, int stream, IMFVideoSampleAllocatorEx allocator, SafeFileHandle? flushHandle)
    {
        _device = device;
        FilePath = path;
        Settings = settings;
        Encoder = encoder;
        _writer = writer;
        _stream = stream;
        _allocator = allocator;
        _flushHandle = flushHandle;
        _converter = new FrameConverter(device, settings.Width, settings.Height);
    }

    public string FilePath { get; }

    public VideoEncoderSettings Settings { get; }

    public H264EncoderInfo Encoder { get; }

    public VideoWriterStatistics Statistics
    {
        get
        {
            long bytes;
            try
            {
                bytes = new FileInfo(FilePath).Length;
            }
            catch (IOException)
            {
                bytes = 0;
            }

            return new VideoWriterStatistics(_submitted, _encoded, bytes, _latencyCount == 0 ? 0 : _latencySumMs / _latencyCount, _latencyMaxMs, _failure);
        }
    }

    /// <summary>
    /// Opens <paramref name="path"/> (replacing it) with the hardware encoder on <paramref name="device"/>'s adapter when
    /// <see cref="VideoEncoderSettings.PreferHardware"/> is on and one opens, else the software encoder.
    /// </summary>
    public static Mp4TrackWriter Create(VideoDevice device, string path, VideoEncoderSettings settings)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(settings);
        MediaFoundationRuntime.EnsureStarted();
        if (settings.PreferHardware)
        {
            try
            {
                return Open(device, path, settings, hardware: true);
            }
            catch (VideoCaptureException ex) when (ex.Code == VideoErrorCode.EncoderUnavailable)
            {
                // Fall through to software: the caller learns which encoder it got from Encoder.IsHardware.
            }
        }

        return Open(device, path, settings, hardware: false);
    }

    private static Mp4TrackWriter Open(VideoDevice device, string path, VideoEncoderSettings settings, bool hardware)
    {
        IMFSinkWriter? writer = null;
        IMFByteStream? byteStream = null;
        IMFVideoSampleAllocatorEx? allocator = null;
        try
        {
            using var attributes = MediaFactory.MFCreateAttributes(4);
            attributes.Set(TranscodeAttributeKeys.TranscodeContainertype, TranscodeContainerTypeGuids.Fmpeg4);
            attributes.Set(SinkWriterAttributeKeys.D3DManager, device.DeviceManager);
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, hardware ? 1u : 0u);
            if (File.Exists(path))
            {
                File.Delete(path);
            }

            // Our own byte stream with write sharing, so Checkpoint can flush the file through a second handle.
            byteStream = MediaFactory.MFCreateFile(FileAccessMode.MfAccessModeWrite, FileOpenMode.MfOpenModeDeleteIfExist, FileFlags.FlagsAllowWriteSharing, path);
            writer = MediaFactory.MFCreateSinkWriterFromURL(path, byteStream, attributes);

            using var output = MediaFactory.MFCreateMediaType();
            output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
            output.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)settings.BitrateBitsPerSecond);
            output.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            output.Set(MediaTypeAttributeKeys.FrameSize, MediaFactory.PackSize((uint)settings.Width, (uint)settings.Height));
            output.Set(MediaTypeAttributeKeys.FrameRate, MediaFactory.PackRatio(settings.FrameRate, 1));
            output.Set(MediaTypeAttributeKeys.PixelAspectRatio, MediaFactory.PackRatio(1, 1));
            output.Set(MediaTypeAttributeKeys.Mpeg2Profile, MediaFoundationGuids.H264ProfileHigh);
            var stream = writer.AddStream(output);

            using var input = MediaFactory.MFCreateMediaType();
            input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            input.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
            input.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
            input.Set(MediaTypeAttributeKeys.FrameSize, MediaFactory.PackSize((uint)settings.Width, (uint)settings.Height));
            input.Set(MediaTypeAttributeKeys.FrameRate, MediaFactory.PackRatio(settings.FrameRate, 1));
            input.Set(MediaTypeAttributeKeys.PixelAspectRatio, MediaFactory.PackRatio(1, 1));

            using var encoding = MediaFactory.MFCreateAttributes(6);
            encoding.Set(MediaFoundationGuids.CodecApiRateControlMode, MediaFoundationGuids.RateControlPeakConstrainedVbr);
            encoding.Set(MediaFoundationGuids.CodecApiMeanBitRate, (uint)settings.BitrateBitsPerSecond);
            encoding.Set(MediaFoundationGuids.CodecApiMaxBitRate, (uint)Math.Min(int.MaxValue, settings.BitrateBitsPerSecond * 2L));
            encoding.Set(MediaFoundationGuids.CodecApiGopSize, (uint)(settings.FrameRate * settings.KeyframeIntervalSeconds));
            encoding.Set(MediaFoundationGuids.CodecApiBPictureCount, 0u);
            encoding.Set(MediaFoundationGuids.CodecApiLowLatencyMode, 1u);
            writer.SetInputMediaType(stream, input, encoding);

            var encoder = IdentifyEncoder(writer, stream, device, hardware);
            if (hardware && !encoder.IsHardware)
            {
                throw new VideoCaptureException(VideoErrorCode.EncoderUnavailable, VideoErrors.NoEncoder());
            }

            allocator = CreateAllocator(device, input);
            writer.BeginWriting();
            var flushHandle = OpenFlushHandle(path);
            var result = new Mp4TrackWriter(device, path, settings, encoder, writer, stream, allocator, flushHandle);
            writer = null;
            allocator = null;
            return result;
        }
        catch (SharpGenException ex)
        {
            throw new VideoCaptureException(VideoErrorCode.EncoderUnavailable, hardware ? VideoErrors.HardwareEncoderFailed() : VideoErrors.NoEncoder(), ex);
        }
        catch (COMException ex)
        {
            throw new VideoCaptureException(VideoErrorCode.EncoderUnavailable, hardware ? VideoErrors.HardwareEncoderFailed() : VideoErrors.NoEncoder(), ex);
        }
        finally
        {
            allocator?.Dispose();
            writer?.Dispose();
            byteStream?.Dispose();
        }
    }

    private static IMFVideoSampleAllocatorEx CreateAllocator(VideoDevice device, IMFMediaType input)
    {
        var allocator = new IMFVideoSampleAllocatorEx(MediaFactory.MFCreateVideoSampleAllocatorEx(typeof(IMFVideoSampleAllocatorEx).GUID));
        allocator.SetDirectXManager(device.DeviceManager);
        using var attributes = MediaFactory.MFCreateAttributes(2);
        attributes.Set(MediaFoundationGuids.SampleAllocatorBindFlags, (uint)(BindFlags.RenderTarget | BindFlags.VideoEncoder));
        attributes.Set(MediaFoundationGuids.SampleAllocatorUsage, (uint)ResourceUsage.Default);
        allocator.InitializeSampleAllocatorEx(2, SurfacePoolSize, attributes, input);
        return allocator;
    }

    /// <summary>
    /// Which encoder the sink writer loaded. A hardware MFT carries a hardware URL or vendor id in its attributes; the
    /// friendly name is often empty there (AMD's is), so the enumerated name for the adapter's vendor is used instead.
    /// </summary>
    private static H264EncoderInfo IdentifyEncoder(IMFSinkWriter writer, int stream, VideoDevice device, bool hardware)
    {
        var software = new H264EncoderInfo("Microsoft H.264 software encoder", false, null, null);
        using var ex = writer.QueryInterfaceOrNull<IMFSinkWriterEx>();
        if (ex is null)
        {
            return hardware ? HardwareInfo(device, null) : software;
        }

        for (var i = 0; i < 8; i++)
        {
            IMFTransform? transform = null;
            try
            {
                ex.GetTransformForStream(stream, i, out var category, out transform);
                if (category != MediaFoundationGuids.VideoEncoderCategory)
                {
                    continue;
                }

                using var attributes = transform.Attributes;
                var url = attributes is null ? null : TryString(attributes, TransformAttributeKeys.MftEnumHardwareUrlAttribute);
                var vendor = attributes is null ? null : TryString(attributes, TransformAttributeKeys.MftEnumHardwareVendorIdAttribute);
                if (string.IsNullOrEmpty(url) && string.IsNullOrEmpty(vendor))
                {
                    return software;
                }

                var name = attributes is null ? null : TryString(attributes, TransformAttributeKeys.MftFriendlyNameAttribute);
                return HardwareInfo(device, string.IsNullOrEmpty(name) ? null : name);
            }
            catch (SharpGenException)
            {
                break;
            }
            finally
            {
                transform?.Dispose();
            }
        }

        return hardware ? HardwareInfo(device, null) : software;
    }

    private static H264EncoderInfo HardwareInfo(VideoDevice device, string? name)
    {
        var listed = H264EncoderProbe.HardwareFor(device.Adapter);
        return new H264EncoderInfo(name ?? listed?.Name ?? $"{device.Adapter.Name} H.264 encoder", true, device.Adapter.Luid, listed?.VendorId);
    }

    private static string? TryString(IMFAttributes attributes, Guid key)
    {
        try
        {
            return attributes.GetString(key);
        }
        catch (SharpGenException)
        {
            return null;
        }
    }

    private static SafeFileHandle? OpenFlushHandle(string path)
    {
        try
        {
            return File.OpenHandle(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    public VideoWriteResult WriteFrame(in VideoFrame frame, long timestampTicks)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_failure is not null || _finalized)
        {
            return VideoWriteResult.Failed;
        }

        IMFSample? sample = null;
        try
        {
            try
            {
                sample = _allocator.AllocateSample();
            }
            catch (SharpGenException ex) when (ex.ResultCode.Code == MediaFoundationGuids.MfESampleAllocatorEmpty)
            {
                return VideoWriteResult.NoFreeSurface;
            }

            using (var buffer = sample.GetBufferByIndex(0))
            using (var dxgi = buffer.QueryInterface<IMFDXGIBuffer>())
            using (var texture = new ID3D11Texture2D(dxgi.GetResource(typeof(ID3D11Texture2D).GUID)))
            {
                var subresource = dxgi.SubresourceIndex;

                // The allocator hands out buffers with a current length of 0; the sink writer rejects them (E_INVALIDARG).
                buffer.CurrentLength = buffer.MaxLength;
                if (frame.Texture is not null)
                {
                    _converter.Convert(frame.Texture, frame.Width, frame.Height, texture, subresource);
                }
            }

            sample.SampleTime = timestampTicks;
            if (_held is not null)
            {
                // The MP4 sinks keep sample durations, not time stamps (ENGINE-NOTES.md §M.4): the held picture lasts
                // until this one, so a skipped slot lengthens it instead of pulling the rest of the track earlier.
                SubmitHeld(Math.Max(1, timestampTicks - _heldTime));
            }

            _firstTimestamp ??= timestampTicks;
            _held = sample;
            _heldTime = timestampTicks;
            sample = null;
            UpdateLatency();
            return VideoWriteResult.Written;
        }
        catch (SharpGenException ex)
        {
            Fail(ex);
            return VideoWriteResult.Failed;
        }
        catch (COMException ex)
        {
            Fail(ex);
            return VideoWriteResult.Failed;
        }
        finally
        {
            sample?.Dispose();
        }
    }

    private void SubmitHeld(long duration)
    {
        var held = _held!;
        _held = null;
        try
        {
            held.SampleDuration = duration;
            _writer.WriteSample(_stream, held);
            _pending.Enqueue((_submitted, QpcClock.Now));
            _submitted++;
            _endTimestamp = _heldTime + duration;
            if (MeasureLatencyBySpinning)
            {
                var deadline = QpcClock.Now + (200 * QpcClock.TicksPerMillisecond);
                while (_encoded < _submitted && QpcClock.Now < deadline)
                {
                    Thread.SpinWait(200);
                    UpdateLatency();
                }
            }
        }
        finally
        {
            held.Dispose();
        }
    }

    /// <summary>
    /// Diagnostics only (VideoCheck): wait after each frame until the encoder reports it done, so the latency figures
    /// are exact instead of rounded up to the next frame. Blocks the pacing thread; never on in the app.
    /// </summary>
    internal static bool MeasureLatencyBySpinning { get; set; }

    /// <summary>Time stamp (100 ns) of the first frame written; the file itself starts at 0 (Media Foundation writes no <c>tfdt</c>).</summary>
    public long? FirstTimestampTicks => _firstTimestamp;

    /// <summary>End (100 ns) of the last frame handed to the encoder.</summary>
    public long EndTimestampTicks => _endTimestamp;

    private void Fail(Exception ex)
    {
        var diskFull = ex.HResult is unchecked((int)0x80070070) or unchecked((int)0x80070027);
        _failure = diskFull
            ? new VideoCaptureException(VideoErrorCode.DiskFull, VideoErrors.DiskFull(Path.GetFileNameWithoutExtension(FilePath)), ex)
            : Encoder.IsHardware
                ? new VideoCaptureException(VideoErrorCode.EncoderFailed, VideoErrors.HardwareEncoderFailed(), ex)
                : new VideoCaptureException(VideoErrorCode.WriteFailed, VideoErrors.WriteFailed(Path.GetFileNameWithoutExtension(FilePath), $"0x{ex.HResult:X8}"), ex);
    }

    /// <summary>
    /// <c>IMFSinkWriter::GetStatistics</c> through the vtable (slot 13): the binding does not fill in the struct's size
    /// field, which the sink writer checks (E_INVALIDARG otherwise).
    /// </summary>
    private unsafe SinkWriterStatistics ReadStatistics()
    {
        var stats = new SinkWriterStatistics { Cb = sizeof(SinkWriterStatistics) };
        var vtable = *(IntPtr**)_writer.NativePointer;
        var getStatistics = (delegate* unmanaged[Stdcall]<IntPtr, int, SinkWriterStatistics*, int>)vtable[13];
        Marshal.ThrowExceptionForHR(getStatistics(_writer.NativePointer, _stream, &stats));
        return stats;
    }

    private void UpdateLatency()
    {
        var stats = ReadStatistics();
        var encoded = (long)stats.NumSamplesEncoded;
        if (encoded <= _encoded)
        {
            return;
        }

        var now = QpcClock.Now;
        while (_pending.Count > 0 && _pending.Peek().Index < encoded)
        {
            var (_, submitted) = _pending.Dequeue();
            var ms = (now - submitted) / (double)QpcClock.TicksPerMillisecond;
            _latencySumMs += ms;
            _latencyCount++;
            _latencyMaxMs = Math.Max(_latencyMaxMs, ms);
        }

        _encoded = encoded;
    }

    public void Checkpoint()
    {
        if (_flushHandle is null || _flushHandle.IsInvalid)
        {
            return;
        }

        _ = FlushFileBuffers(_flushHandle);
    }

    public Task FinalizeAsync(CancellationToken cancellationToken)
    {
        if (_finalized || _disposed)
        {
            return Task.CompletedTask;
        }

        _finalized = true;
        return Task.Run(
            () =>
            {
                var watch = Stopwatch.StartNew();
                try
                {
                    if (_held is not null)
                    {
                        SubmitHeld(Settings.FrameDurationTicks);
                    }

                    _writer.Finalize();
                    var stats = ReadStatistics();
                    _encoded = (long)stats.NumSamplesEncoded;
                }
                catch (SharpGenException ex)
                {
                    Fail(ex);
                }

                FinalizeMilliseconds = watch.Elapsed.TotalMilliseconds;
                Checkpoint();
            },
            cancellationToken);
    }

    /// <summary>How long <see cref="FinalizeAsync"/> took (drain + close), for the spike's numbers.</summary>
    public double FinalizeMilliseconds { get; private set; }

    public ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return ValueTask.CompletedTask;
        }

        _disposed = true;
        _held?.Dispose();
        _held = null;
        _converter.Dispose();
        _allocator.UninitializeSampleAllocator();
        _allocator.Dispose();
        _writer.Dispose();
        _flushHandle?.Dispose();
        return ValueTask.CompletedTask;
    }

    [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FlushFileBuffers(SafeFileHandle handle);
}
