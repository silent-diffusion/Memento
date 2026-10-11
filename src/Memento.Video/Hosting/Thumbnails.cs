using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Memento.Core.Bridge.Contracts;
using Memento.Video.Gpu;
using Memento.Video.Interop;
using Memento.Video.Export;
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
    public static VideoThumbnailResult Take(VideoSourceId id, VideoSourceInfo info, int width, Func<Nv12Frame?>? recording = null)
    {
        using var picture = id.Kind switch
        {
            VideoSourceKind.Display => Display(id, info),
            VideoSourceKind.Window => Window(id, info),
            _ => Camera(id, info, recording),
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

    /// <summary>
    /// A camera's picture: the running track's latest one when it is being recorded (a camera opens only once), else
    /// the next one from <see cref="CameraStills"/>, which keeps the camera open while the page keeps asking.
    /// </summary>
    private static Bitmap Camera(VideoSourceId id, VideoSourceInfo info, Func<Nv12Frame?>? recording)
    {
        var frame = recording?.Invoke() ?? CameraStills.Take(id, info.Name);
        return ToBitmap(frame);
    }

    /// <summary>NV12 (BT.709, limited range) to a 32-bit bitmap.</summary>
    internal static unsafe Bitmap ToBitmap(Nv12Frame frame)
    {
        var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppRgb);
        var data = bitmap.LockBits(new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppRgb);
        try
        {
            var source = frame.Data;
            var chroma = frame.Width * frame.Height;
            for (var y = 0; y < frame.Height; y++)
            {
                var row = (byte*)data.Scan0 + ((long)y * data.Stride);
                for (var x = 0; x < frame.Width; x++)
                {
                    var c = (source[(y * frame.Width) + x] - 16) * 1.164;
                    var uv = chroma + ((y / 2) * frame.Width) + ((x / 2) * 2);
                    var d = source[uv] - 128;
                    var e = source[uv + 1] - 128;
                    row[(x * 4) + 2] = Clamp(c + (1.793 * e));
                    row[(x * 4) + 1] = Clamp(c - (0.213 * d) - (0.533 * e));
                    row[x * 4] = Clamp(c + (2.112 * d));
                    row[(x * 4) + 3] = 255;
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }

    private static byte Clamp(double value) => (byte)Math.Clamp((int)Math.Round(value), 0, 255);

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
