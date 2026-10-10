using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Memento.Core.Bridge.Contracts;
using Memento.Video.Gpu;
using Memento.Video.Interop;
using Memento.Video.Sources;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Memento.Video.Hosting;

/// <summary>
/// One small still of a display, window or camera for the picker (<c>video.thumbnail</c>), as a JPEG data URL. A display
/// is read with GDI and a window with <c>PrintWindow</c>, so no capture session (and no yellow border) is started; a
/// camera is opened for a few frames and closed. Nothing is written to disk. Windows that keep themselves out of
/// captures are never read (the caller refuses them first).
/// </summary>
internal static partial class Thumbnails
{
    private const int CameraFramesToSkip = 5;

    public static VideoThumbnailResult Take(VideoSourceId id, VideoSourceInfo info, int width)
    {
        using var picture = id.Kind switch
        {
            VideoSourceKind.Display => Display(id, info),
            VideoSourceKind.Window => Window(id, info),
            _ => Camera(id, info),
        };
        return Encode(picture, width);
    }

    private static Bitmap Display(VideoSourceId id, VideoSourceInfo info)
    {
        var mode = User32.CurrentMode(id.Key) ?? throw new VideoCaptureException(VideoErrorCode.SourceNotFound, VideoErrors.DisplayNotFound(info.Name));
        var bitmap = new Bitmap(Math.Max(1, mode.Width), Math.Max(1, mode.Height), PixelFormat.Format32bppRgb);
        try
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(mode.Left, mode.Top, 0, 0, new Size(mode.Width, mode.Height), CopyPixelOperation.SourceCopy);
            return bitmap;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or ExternalException)
        {
            bitmap.Dispose();
            throw new VideoCaptureException(VideoErrorCode.SourceNotFound, $"{info.Name} could not be read for its thumbnail. Audio recording is not affected.", ex);
        }
    }

    private static Bitmap Window(VideoSourceId id, VideoSourceInfo info)
    {
        var hwnd = id.WindowHandle;
        if (hwnd == IntPtr.Zero || !User32.IsWindow(hwnd) || !User32.GetWindowRect(hwnd, out var rect))
        {
            throw new VideoCaptureException(VideoErrorCode.SourceNotFound, VideoErrors.WindowNotFound(info.Name));
        }

        if (User32.DisplayAffinity(hwnd) != User32.WdaNone)
        {
            throw new VideoCaptureException(VideoErrorCode.ProtectedContent, VideoErrors.ProtectedWindow(info.Name));
        }

        var bitmap = new Bitmap(Math.Clamp(rect.Width, 1, 8192), Math.Clamp(rect.Height, 1, 8192), PixelFormat.Format32bppRgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(0x2A, 0x28, 0x24));
            if (!User32.IsIconic(hwnd))
            {
                var hdc = graphics.GetHdc();
                try
                {
                    // PW_RENDERFULLCONTENT: windows drawn by DirectComposition (browsers, modern apps) too.
                    _ = PrintWindow(hwnd, hdc, 2);
                }
                finally
                {
                    graphics.ReleaseHdc(hdc);
                }
            }
        }

        return bitmap;
    }

    private static unsafe Bitmap Camera(VideoSourceId id, VideoSourceInfo info)
    {
        var access = CameraPrivacy.Read();
        if (access is CameraAccess.DeniedForPc or CameraAccess.DeniedForUser or CameraAccess.DeniedForDesktopApps)
        {
            throw CameraErrors.Classify(CameraErrors.AccessDenied, info.Name, access, null);
        }

        MediaFoundationRuntime.EnsureStarted();
        IMFMediaSource? source = null;
        IMFSourceReader? reader = null;
        try
        {
            using (var attributes = MediaFactory.MFCreateAttributes(2))
            {
                attributes.Set(CaptureDeviceAttributeKeys.SourceType, CaptureDeviceAttributeKeys.SourceTypeVidcap);
                attributes.Set(CaptureDeviceAttributeKeys.SourceTypeVidcapSymbolicLink, id.Key);
                source = MediaFactory.MFCreateDeviceSource(attributes);
            }

            using (var attributes = MediaFactory.MFCreateAttributes(2))
            {
                attributes.Set(SourceReaderAttributeKeys.EnableVideoProcessing, 1u);
                attributes.Set(SourceReaderAttributeKeys.DisconnectMediasourceOnShutdown, 1u);
                reader = MediaFactory.MFCreateSourceReaderFromMediaSource(source, attributes);
            }

            using (var type = MediaFactory.MFCreateMediaType())
            {
                type.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
                type.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.Rgb32);
                reader.SetCurrentMediaType(SourceReaderIndex.FirstVideoStream, type);
            }

            using var current = reader.GetCurrentMediaType(SourceReaderIndex.FirstVideoStream);
            var size = current.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            var width = (int)(size >> 32);
            var height = (int)(size & 0xFFFFFFFF);

            // The first frames of many cameras are dark while exposure settles.
            for (var i = 0; ; i++)
            {
                using var sample = reader.ReadSample(SourceReaderIndex.FirstVideoStream, SourceReaderControlFlag.None, out _, out var flags, out _);
                if ((flags & SourceReaderFlag.EndOfStream) != 0)
                {
                    throw new VideoCaptureException(VideoErrorCode.SourceNotFound, VideoErrors.CameraNotFound(info.Name));
                }

                if (sample is null || i < CameraFramesToSkip)
                {
                    continue;
                }

                using var buffer = sample.ConvertToContiguousBuffer();
                buffer.Lock(out var pointer, out _, out var length);
                try
                {
                    var bitmap = new Bitmap(width, height, PixelFormat.Format32bppRgb);
                    var data = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
                    try
                    {
                        var rowBytes = width * 4;
                        for (var y = 0; y < height && ((y + 1) * rowBytes) <= length; y++)
                        {
                            Buffer.MemoryCopy((byte*)pointer + ((long)y * rowBytes), (byte*)data.Scan0 + ((long)y * data.Stride), data.Stride, rowBytes);
                        }
                    }
                    finally
                    {
                        bitmap.UnlockBits(data);
                    }

                    return bitmap;
                }
                finally
                {
                    buffer.Unlock();
                }
            }
        }
        catch (SharpGenException ex)
        {
            throw CameraErrors.Classify(ex.ResultCode.Code, info.Name, CameraPrivacy.Read(), CameraUsage.AppUsingCamera(Environment.ProcessPath), inner: ex);
        }
        finally
        {
            reader?.Dispose();
            if (source is not null)
            {
                source.Shutdown();
                source.Dispose();
            }
        }
    }

    private static VideoThumbnailResult Encode(Bitmap picture, int width)
    {
        var height = Math.Max(1, (int)Math.Round((double)width * picture.Height / Math.Max(1, picture.Width)));
        using var small = new Bitmap(width, height, PixelFormat.Format24bppRgb);
        using (var graphics = Graphics.FromImage(small))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(picture, 0, 0, width, height);
        }

        var jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
        using var parameters = new EncoderParameters(1);
        parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 80L);
        using var stream = new MemoryStream();
        small.Save(stream, jpeg, parameters);
        return new VideoThumbnailResult("data:image/jpeg;base64," + Convert.ToBase64String(stream.GetBuffer(), 0, (int)stream.Length), width, height);
    }

    [LibraryImport("user32.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
}
