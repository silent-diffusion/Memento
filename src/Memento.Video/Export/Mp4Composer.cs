using Memento.Core.Video;
using Memento.Video.Gpu;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Memento.Video.Export;

/// <summary>
/// Writes the video files of an export and joins a track's parts (DESIGN.md §18.4, §18.7), with Media Foundation and
/// the processor only, so it runs beside a recording without touching the graphics card's capture work:
/// <list type="bullet">
/// <item><b>Encode</b> (MP4): every output frame at the track's frame rate on the recording timeline is composed from the
/// latest decoded picture of the main track (black before its first frame and after its end), with the camera inset
/// drawn where the inset was at that time, then encoded as H.264 (the graphics card's encoder when it takes system
/// memory pictures, else Microsoft's), with the mix as AAC.</item>
/// <item><b>Copy</b> ("Original container"): the recorded H.264 samples are copied untouched, shifted by the track's
/// start, with the mix as AAC.</item>
/// </list>
/// Audio and video samples are written in time order, so the file is interleaved. The output goes to a temporary file
/// that only a finished compose moves into place.
/// </summary>
internal static class Mp4Composer
{
    /// <summary>What a compose wrote.</summary>
    public sealed record Result(long Frames, long DurationTicks, long Bytes, bool Hardware);

    public static Task<Result> ComposeAsync(VideoComposeRequest request, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var main = new[] { new ComposeSegment(request.Video.Path, request.Video.StartOffsetMs * TimeSpan.TicksPerMillisecond) };
        var duration = request.DurationMs * TimeSpan.TicksPerMillisecond;
        return Task.Run(
            () => request.Reencode
                ? Encode(request.OutputPath, main, request.Video.Width, request.Video.Height, request.Video.FrameRate, Bitrate(request.Video), request.Inset, request.Pip, request.AudioPath, duration, progress, cancellationToken)
                : Copy(request.OutputPath, main, request.AudioPath, duration, progress, cancellationToken),
            cancellationToken);
    }

    /// <summary>Encodes <paramref name="main"/> (with the inset and the mix when given) into <paramref name="output"/>.</summary>
    public static Result Encode(
        string output,
        IReadOnlyList<ComposeSegment> main,
        int width,
        int height,
        int frameRate,
        int bitsPerSecond,
        VideoComposeInput? inset,
        IReadOnlyList<VideoPipChange> pip,
        string? audioPath,
        long durationTicks,
        IProgress<double>? progress,
        CancellationToken cancellationToken,
        bool preferHardware = true)
    {
        try
        {
            return EncodeOnce(output, main, width, height, frameRate, bitsPerSecond, inset, pip, audioPath, durationTicks, progress, preferHardware, cancellationToken);
        }
        catch (VideoCaptureException) when (!cancellationToken.IsCancellationRequested && preferHardware)
        {
            // A hardware encoder that refused system-memory pictures part-way: start again in software.
            return EncodeOnce(output, main, width, height, frameRate, bitsPerSecond, inset, pip, audioPath, durationTicks, progress, preferHardware: false, cancellationToken);
        }
    }

    /// <summary>Copies <paramref name="main"/>'s samples untouched (with the mix when given) into <paramref name="output"/>.</summary>
    public static Result Copy(string output, IReadOnlyList<ComposeSegment> main, string? audioPath, long durationTicks, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        MediaFoundationRuntime.EnsureStarted();
        using var audio = audioPath is null ? null : new AudioPcmReader(audioPath);
        IMFSourceReader? reader = null;
        Mp4OutputWriter? writer = null;
        byte[]? header = null;
        long frames = 0;
        long end = 0;
        try
        {
            IMFSample? pendingAudio = audio?.Next();
            foreach (var segment in main)
            {
                reader?.Dispose();
                reader = MediaFactory.MFCreateSourceReaderFromURL(segment.Path, null);
                reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
                reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
                using var native = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
                var sequence = TryBlob(native, MediaTypeAttributeKeys.MpegSequenceHeader);
                if (writer is null)
                {
                    writer = Mp4OutputWriter.Copying(output, native, audio);
                    header = sequence;
                }
                else if (!SameBytes(header, sequence))
                {
                    throw new VideoCaptureException(VideoErrorCode.WriteFailed, "These video parts were encoded differently (another encoder took over) and can't be joined without encoding again.");
                }

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out var time);
                    if ((flags & SourceReaderFlag.EndOfStream) != 0 || sample is null)
                    {
                        break;
                    }

                    var at = time + segment.StartTicks;
                    pendingAudio = WriteAudioUntil(writer, audio, pendingAudio, at);
                    sample.SampleTime = at;
                    writer.WriteVideoSample(sample);
                    frames++;
                    end = Math.Max(end, at + sample.SampleDuration);
                    if (frames % 30 == 0)
                    {
                        progress?.Report(Math.Min(0.99, (double)at / Math.Max(1, durationTicks)));
                    }
                }
            }

            if (writer is null)
            {
                throw new VideoCaptureException(VideoErrorCode.WriteFailed, "There was no video to copy.");
            }

            WriteAudioUntil(writer, audio, pendingAudio, long.MaxValue);
            writer.Finish();
            progress?.Report(1);
            return new Result(frames, Math.Max(end, audio?.Position ?? 0), new FileInfo(output).Length, false);
        }
        catch (SharpGenException ex)
        {
            throw new VideoCaptureException(VideoErrorCode.WriteFailed, $"The video file could not be copied (0x{ex.HResult:X8}). The recorded file is kept unchanged.", ex);
        }
        finally
        {
            reader?.Dispose();
            writer?.Dispose();
        }
    }

    /// <summary>Which output frame times show the inset, and where: the last change at or before each time wins.</summary>
    internal static VideoPipChange PipAt(IReadOnlyList<VideoPipChange> pip, long atMs)
    {
        var current = pip.Count > 0 ? pip[0] : new VideoPipChange(0, true, VideoPipChange.BottomRight);
        foreach (var change in pip)
        {
            if (change.AtMs <= atMs)
            {
                current = change;
            }
        }

        return current;
    }

    private static int Bitrate(VideoComposeInput input) =>
        input.BitrateBitsPerSecond > 0 ? input.BitrateBitsPerSecond : Encoding.VideoBitrate.For(input.Width, input.Height, input.FrameRate, Encoding.VideoQuality.Balanced);

    private static Result EncodeOnce(
        string output,
        IReadOnlyList<ComposeSegment> main,
        int width,
        int height,
        int frameRate,
        int bitsPerSecond,
        VideoComposeInput? inset,
        IReadOnlyList<VideoPipChange> pip,
        string? audioPath,
        long durationTicks,
        IProgress<double>? progress,
        bool preferHardware,
        CancellationToken cancellationToken)
    {
        (width, height) = (Math.Max(64, width & ~1), Math.Max(64, height & ~1));
        frameRate = Math.Clamp(frameRate, 1, 120);
        using var audio = audioPath is null ? null : new AudioPcmReader(audioPath);
        using var mainReader = new Nv12SegmentReader(main);
        using var insetReader = inset is null ? null : new Nv12SegmentReader([new ComposeSegment(inset.Path, inset.StartOffsetMs * TimeSpan.TicksPerMillisecond)]);
        using var writer = Mp4OutputWriter.Encoding(output, width, height, frameRate, bitsPerSecond, audio, preferHardware);

        var period = TimeSpan.TicksPerSecond / frameRate;
        var total = Math.Max(1, (long)Math.Round(durationTicks * (double)frameRate / TimeSpan.TicksPerSecond));
        var frame = new Nv12Frame(width, height);
        var mainTrack = new TrackCursor(mainReader, width, height, period);
        var insetTrack = inset is null || insetReader is null ? null : new TrackCursor(insetReader, Math.Max(2, inset.Width & ~1), Math.Max(2, inset.Height & ~1), TimeSpan.TicksPerSecond / Math.Clamp(inset.FrameRate, 1, 120));
        IMFSample? pendingAudio = audio?.Next();
        long frames = 0;
        for (long k = 0; k < total; k++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // Exact frame times (k / fps), so an hour of 30 fps does not drift by the rounding of one period.
            var t = k * TimeSpan.TicksPerSecond / frameRate;
            var next = (k + 1) * TimeSpan.TicksPerSecond / frameRate;
            if (mainTrack.At(t) is { } picture)
            {
                picture.CopyTo(frame);
            }
            else
            {
                frame.FillBlack();
            }

            if (insetTrack?.At(t) is { } camera && PipAt(pip, t / TimeSpan.TicksPerMillisecond) is { Enabled: true } where)
            {
                var (x, y, w, h) = Nv12Frame.InsetRectangle(width, height, camera.Width, camera.Height, where.Corner);
                frame.DrawScaled(camera.Frame, x, y, w, h);
            }

            pendingAudio = WriteAudioUntil(writer, audio, pendingAudio, next);
            writer.WriteVideo(frame, t, next - t);
            frames++;
            if (k % 30 == 0)
            {
                progress?.Report(Math.Min(0.99, (double)k / total));
            }
        }

        WriteAudioUntil(writer, audio, pendingAudio, long.MaxValue);
        writer.Finish();
        progress?.Report(1);
        return new Result(frames, total * TimeSpan.TicksPerSecond / frameRate, new FileInfo(output).Length, writer.Hardware);
    }

    /// <summary>Writes the audio blocks that start before <paramref name="until"/>; returns the first one left over.</summary>
    private static IMFSample? WriteAudioUntil(Mp4OutputWriter writer, AudioPcmReader? audio, IMFSample? pending, long until)
    {
        while (audio is not null && pending is not null && pending.SampleTime < until)
        {
            writer.WriteAudio(pending);
            pending.Dispose();
            pending = audio.Next();
        }

        return pending;
    }

    private static byte[]? TryBlob(IMFAttributes attributes, Guid key)
    {
        try
        {
            return attributes.GetBlob(key);
        }
        catch (SharpGenException)
        {
            return null;
        }
    }

    private static bool SameBytes(byte[]? a, byte[]? b) =>
        (a is null && b is null) || (a is not null && b is not null && a.AsSpan().SequenceEqual(b));

    /// <summary>
    /// Walks a track's decoded pictures along the output's frame times: the picture shown at time t is the last one
    /// that starts at or before t, until its own end (a gap in the track holds the frame before it, as the recording did).
    /// </summary>
    private sealed class TrackCursor(Nv12SegmentReader reader, int width, int height, long framePeriod)
    {
        private Picture? _current;
        private Picture? _next;
        private bool _ended;

        /// <summary>The picture to show at <paramref name="t"/>, or null before the track's first picture and after its end.</summary>
        public Picture? At(long t)
        {
            _next ??= Read();
            while (_next is not null && _next.Time <= t)
            {
                // Recycle the old picture's buffer for the next read.
                var spare = _current;
                _current = _next;
                _next = Read(spare);
            }

            return _current is { } current && t < current.Time + (current.Duration > 0 ? current.Duration : framePeriod) ? current : null;
        }

        private Picture? Read(Picture? recycle = null)
        {
            if (_ended)
            {
                return null;
            }

            var target = recycle ?? new Picture(width, height);
            if (!reader.TryRead(target.Frame, out var time, out var duration))
            {
                _ended = true;
                return null;
            }

            target.Time = time;
            target.Duration = duration;
            return target;
        }
    }

    private sealed class Picture(int width, int height)
    {
        public Nv12Frame Frame { get; } = new(width, height);

        public long Time { get; set; }

        public long Duration { get; set; }

        public int Width => Frame.Width;

        public int Height => Frame.Height;

        public void CopyTo(Nv12Frame target)
        {
            if (Frame.Width == target.Width && Frame.Height == target.Height)
            {
                Frame.Data.CopyTo(target.Data, 0);
            }
            else
            {
                target.DrawLetterboxed(Frame);
            }
        }

    }
}
