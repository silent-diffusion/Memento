using Memento.Video.Export;
using Memento.Video.Gpu;
using Memento.Video.Sources;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Memento.Video.Hosting;

/// <summary>
/// Camera thumbnails for the Recording screen, which asks for one every second while it shows the camera row: the
/// camera is opened on the first request and kept open while the requests keep coming, then closed
/// <see cref="IdleAfter"/> after the last one (so the camera light goes out). Before a camera track starts, its camera
/// is released here (<see cref="Release"/>), since a camera opens only once.
/// </summary>
internal static class CameraStills
{
    public static readonly TimeSpan IdleAfter = TimeSpan.FromSeconds(3);

    private const int FramesToSkipAfterOpening = 5;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Still> Readers = new(StringComparer.Ordinal);
    private static Timer? _sweeper;

    /// <summary>The camera's next picture as NV12; throws <see cref="VideoCaptureException"/> (privacy, in use, not connected).</summary>
    public static Nv12Frame Take(VideoSourceId id, string name)
    {
        lock (Gate)
        {
            if (!Readers.TryGetValue(id.Key, out var open))
            {
                open = id.Backend == CameraBackend.DirectShow ? DirectShowStill.Start(id, name) : MediaFoundationStill.Start(id, name);
                Readers[id.Key] = open;
                _sweeper ??= new Timer(_ => Sweep(), null, IdleAfter, IdleAfter);
            }

            open.LastUsed = DateTime.UtcNow;
            try
            {
                return open.Read(name);
            }
            catch (VideoCaptureException)
            {
                Readers.Remove(id.Key);
                open.Dispose();
                throw;
            }
        }
    }

    /// <summary>Closes the camera if a thumbnail holds it (a recording is about to open it).</summary>
    public static void Release(VideoSourceId id)
    {
        lock (Gate)
        {
            if (Readers.Remove(id.Key, out var open))
            {
                open.Dispose();
            }
        }
    }

    private static void Sweep()
    {
        lock (Gate)
        {
            foreach (var (key, open) in Readers.Where(kv => DateTime.UtcNow - kv.Value.LastUsed > IdleAfter).ToList())
            {
                Readers.Remove(key);
                open.Dispose();
            }

            if (Readers.Count == 0)
            {
                _sweeper?.Dispose();
                _sweeper = null;
            }
        }
    }

    private sealed class MediaFoundationStill(IMFMediaSource source, IMFSourceReader reader, int width, int height) : Still
    {
        private int _skip = FramesToSkipAfterOpening;

        public static MediaFoundationStill Start(VideoSourceId id, string name)
        {
            var access = CameraPrivacy.Read();
            if (access is CameraAccess.DeniedForPc or CameraAccess.DeniedForUser or CameraAccess.DeniedForDesktopApps)
            {
                throw CameraErrors.Classify(CameraErrors.AccessDenied, name, access, null);
            }

            MediaFoundationRuntime.EnsureStarted();
            IMFMediaSource? mediaSource = null;
            IMFSourceReader? sourceReader = null;
            try
            {
                using (var attributes = MediaFactory.MFCreateAttributes(2))
                {
                    attributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVidcap);
                    attributes.Set(CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink, id.Key);
                    mediaSource = MediaFactory.MFCreateDeviceSource(attributes);
                }

                using (var attributes = MediaFactory.MFCreateAttributes(2))
                {
                    attributes.Set(SourceReaderAttributeKeys.EnableAdvancedVideoProcessing, 1u);
                    attributes.Set(SourceReaderAttributeKeys.DisconnectMediasourceOnShutdown, 1u);
                    sourceReader = MediaFactory.MFCreateSourceReaderFromMediaSource(mediaSource, attributes);
                }

                using (var type = MediaFactory.MFCreateMediaType())
                {
                    type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                    type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
                    sourceReader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, type);
                }

                using var current = sourceReader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
                var size = current.GetUInt64(MediaTypeAttributeKeys.FrameSize);
                var result = new MediaFoundationStill(mediaSource, sourceReader, (int)(size >> 32) & ~1, (int)(size & 0xFFFFFFFF) & ~1);
                mediaSource = null;
                sourceReader = null;
                return result;
            }
            catch (SharpGenException ex)
            {
                throw CameraErrors.Classify(ex.ResultCode.Code, name, CameraPrivacy.Read(), CameraUsage.AppUsingCamera(Environment.ProcessPath), inner: ex);
            }
            finally
            {
                sourceReader?.Dispose();
                if (mediaSource is not null)
                {
                    mediaSource.Shutdown();
                    mediaSource.Dispose();
                }
            }
        }

        public override Nv12Frame Read(string name)
        {
            try
            {
                while (true)
                {
                    using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out _);
                    if ((flags & (SourceReaderFlag.EndOfStream | SourceReaderFlag.Error)) != 0)
                    {
                        throw new VideoCaptureException(VideoErrorCode.SourceNotFound, VideoErrors.CameraNotFound(name));
                    }

                    if (sample is null || _skip-- > 0)
                    {
                        continue;
                    }

                    using var buffer = sample.ConvertToContiguousBuffer();
                    buffer.Lock(out var pointer, out _, out _);
                    try
                    {
                        var frame = new Nv12Frame(width, height);
                        frame.CopyFrom(pointer, width, width, height);
                        return frame;
                    }
                    finally
                    {
                        buffer.Unlock();
                    }
                }
            }
            catch (SharpGenException ex)
            {
                throw CameraErrors.Classify(ex.ResultCode.Code, name, CameraPrivacy.Read(), CameraUsage.AppUsingCamera(Environment.ProcessPath), inner: ex);
            }
        }

        public override void Dispose()
        {
            reader.Dispose();
            try
            {
                source.Shutdown();
            }
            catch (SharpGenException)
            {
                // Already gone.
            }

            source.Dispose();
        }
    }

    /// <summary>A camera held open for thumbnails.</summary>
    private abstract class Still : IDisposable
    {
        public DateTime LastUsed { get; set; }

        /// <summary>The camera's latest picture; throws <see cref="VideoCaptureException"/> when it went away.</summary>
        public abstract Nv12Frame Read(string name);

        public abstract void Dispose();
    }

    /// <summary>
    /// A DirectShow camera held open for thumbnails (ENGINE-NOTES.md §R): the same graph as a recording, at up to
    /// 1280 × 720, feeding a two-buffer queue; each request takes the newest picture waiting (or waits for the next).
    /// A virtual camera whose app is not sending shows what its filter shows (OBS's placeholder picture).
    /// </summary>
    private sealed class DirectShowStill : Still
    {
        private static readonly TimeSpan FirstPictureWait = TimeSpan.FromSeconds(4);

        private readonly DirectShowGraph _graph;
        private readonly RawFrameQueue _queue = new(slots: 2);
        private readonly (int Width, int Height) _size;
        private volatile int _error;

        private DirectShowStill(DirectShowGraph graph)
        {
            _graph = graph;

            _size = graph.Format.EvenSize;
        }

        public static DirectShowStill Start(VideoSourceId id, string name)
        {
            if (DirectShowGraph.IsHardware(id.Key))
            {
                var access = CameraPrivacy.Read();
                if (access is CameraAccess.DeniedForPc or CameraAccess.DeniedForUser or CameraAccess.DeniedForDesktopApps)
                {
                    throw CameraErrors.Classify(CameraErrors.AccessDenied, name, access, null);
                }
            }

            var graph = DirectShowGraph.Open(id.Key, name, maxWidth: 1280, maxHeight: 720, minFrameRate: 1);
            var still = new DirectShowStill(graph);
            try
            {
                graph.Start(still.OnFrame, hr => still._error = hr);
                return still;
            }
            catch
            {
                still.Dispose();
                throw;
            }
        }

        private unsafe void OnFrame(IntPtr data, int length) =>
            _queue.TryWrite(new ReadOnlySpan<byte>((void*)data, length), Audio.QpcClock.Now);

        public override Nv12Frame Read(string name)
        {
            RawFrame? newest = null;
            while (_queue.Take(TimeSpan.Zero) is { } waiting)
            {
                if (newest is not null)
                {
                    _queue.Return(newest);
                }

                newest = waiting;
            }

            newest ??= _queue.Take(FirstPictureWait);
            if (_error != 0)
            {
                if (newest is not null)
                {
                    _queue.Return(newest);
                }

                throw CameraErrors.Classify(_error, name, CameraAccess.Unknown, null);
            }

            if (newest is null)
            {
                throw new VideoCaptureException(VideoErrorCode.SourceNotFound, $"{name} sent no picture for its thumbnail. Audio recording is not affected. Check the camera's own app, or pick another camera.");
            }

            try
            {
                var copy = new Nv12Frame(_size.Width, _size.Height);
                if (!RawFrameConverter.ToNv12(_graph.Format, newest.Buffer.AsSpan(0, newest.Length), copy))
                {
                    throw new VideoCaptureException(VideoErrorCode.SourceNotFound, VideoErrors.CameraFormatRefused(name, _graph.Format.SubtypeName));
                }

                return copy;
            }
            finally
            {
                _queue.Return(newest);
            }
        }

        public override void Dispose()
        {
            _graph.Dispose();
            _queue.Dispose();
        }
    }
}
