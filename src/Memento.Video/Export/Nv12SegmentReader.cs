using Memento.Video.Gpu;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Memento.Video.Export;

/// <summary>
/// Decodes the pictures of one video track, part after part, to NV12 on the processor (Media Foundation's H.264
/// decoder), each with its time on the recording timeline: the sample's own time in its file plus the part's start.
/// Only one part is open at a time.
/// </summary>
internal sealed class Nv12SegmentReader : IDisposable
{
    private readonly IReadOnlyList<ComposeSegment> _segments;
    private int _index = -1;
    private IMFSourceReader? _reader;
    private int _frameWidth;
    private int _frameHeight;
    private int _stride;

    public Nv12SegmentReader(IReadOnlyList<ComposeSegment> segments)
    {
        _segments = segments;
        MediaFoundationRuntime.EnsureStarted();
    }

    /// <summary>Pictures decoded so far.</summary>
    public long Frames { get; private set; }

    /// <summary>Decodes the next picture into <paramref name="target"/>; false at the end of the last part.</summary>
    /// <param name="time">Its time on the recording timeline (100 ns).</param>
    /// <param name="duration">How long it is shown (100 ns).</param>
    public bool TryRead(Nv12Frame target, out long time, out long duration)
    {
        while (true)
        {
            if (_reader is null && !OpenNext())
            {
                time = 0;
                duration = 0;
                return false;
            }

            IMFSample? sample;
            SourceReaderFlag flags;
            long sampleTime;
            try
            {
                sample = _reader!.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out flags, out sampleTime);
            }
            catch (SharpGenException ex)
            {
                throw new VideoCaptureException(VideoErrorCode.WriteFailed, $"A video file could not be read ({Path.GetFileName(_segments[_index].Path)}, 0x{ex.HResult:X8}). The recorded file is kept unchanged.", ex);
            }

            using (sample)
            {
                if ((flags & SourceReaderFlag.CurrentMediaTypeChanged) != 0)
                {
                    ReadFormat();
                }

                if ((flags & SourceReaderFlag.EndOfStream) != 0 || sample is null)
                {
                    if ((flags & SourceReaderFlag.EndOfStream) != 0)
                    {
                        Close();
                    }

                    continue;
                }

                time = sampleTime + _segments[_index].StartTicks;
                duration = sample.SampleDuration;
                Copy(sample, target);
                Frames++;
                return true;
            }
        }
    }

    public void Dispose() => Close();

    private bool OpenNext()
    {
        if (_index + 1 >= _segments.Count)
        {
            return false;
        }

        _index++;
        try
        {
            using var attributes = MediaFactory.MFCreateAttributes(1);
            attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, 1u);
            _reader = MediaFactory.MFCreateSourceReaderFromURL(_segments[_index].Path, attributes);
            _reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            _reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
            using var type = MediaFactory.MFCreateMediaType();
            type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
            type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
            _reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, type);
            ReadFormat();
            return true;
        }
        catch (SharpGenException ex)
        {
            Close();
            throw new VideoCaptureException(VideoErrorCode.WriteFailed, $"A video file could not be opened ({Path.GetFileName(_segments[_index].Path)}, 0x{ex.HResult:X8}). The recorded file is kept unchanged.", ex);
        }
    }

    private void ReadFormat()
    {
        using var type = _reader!.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
        var size = type.GetUInt64(MediaTypeAttributeKeys.FrameSize);
        _frameWidth = (int)(size >> 32);
        _frameHeight = (int)(size & 0xFFFFFFFF);
        _stride = _frameWidth;
        if (type.GetUInt32(MediaTypeAttributeKeys.DefaultStride, out var stride).Success && (int)stride > 0)
        {
            _stride = (int)stride;
        }
    }

    private void Copy(IMFSample sample, Nv12Frame target)
    {
        using var buffer = sample.GetBufferByIndex(0);
        using var buffer2d = buffer.QueryInterfaceOrNull<IMF2DBuffer>();
        if (buffer2d is not null)
        {
            buffer2d.Lock2D(out var scanline, out var pitch);
            try
            {
                Fit(target, scanline, pitch);
            }
            finally
            {
                buffer2d.Unlock2D();
            }

            return;
        }

        buffer.Lock(out var pointer, out _, out _);
        try
        {
            Fit(target, pointer, _stride);
        }
        finally
        {
            buffer.Unlock();
        }
    }

    private Nv12Frame? _scratch;

    /// <summary>The usual case copies straight in; a picture of another size (a resized window track) is letterboxed.</summary>
    private void Fit(Nv12Frame target, IntPtr pointer, int pitch)
    {
        var width = _frameWidth & ~1;
        var height = _frameHeight & ~1;
        if (width >= target.Width && height >= target.Height && width - target.Width < 16 && height - target.Height < 16)
        {
            target.CopyFrom(pointer, pitch, _frameWidth, _frameHeight);
            return;
        }

        if (_scratch is null || _scratch.Width != width || _scratch.Height != height)
        {
            _scratch = new Nv12Frame(width, height);
        }

        _scratch.CopyFrom(pointer, pitch, _frameWidth, _frameHeight);
        target.DrawLetterboxed(_scratch);
    }

    private void Close()
    {
        _reader?.Dispose();
        _reader = null;
    }
}
