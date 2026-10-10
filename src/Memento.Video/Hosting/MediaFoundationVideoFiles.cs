using Memento.Core.Video;
using Memento.Video.Export;
using Memento.Video.Gpu;
using Memento.Video.Recovery;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Memento.Video.Hosting;

/// <summary>The app's <see cref="IVideoFiles"/>: recovery repair, joining parts and export files through Media Foundation.</summary>
public sealed class MediaFoundationVideoFiles : IVideoFiles
{
    public bool IsAvailable => true;

    public VideoFileRepair Repair(string path)
    {
        var repaired = VideoRecovery.Repair(path);
        if (repaired.Readable)
        {
            return new VideoFileRepair(true, repaired.Samples, (long)(repaired.DurationSeconds * 1000), repaired.TornBytesRemoved);
        }

        // An ordinary MP4 (a joined file) needs no repair; it is readable when it has its index.
        var summary = Mp4FileSummary.Read(path);
        return new VideoFileRepair(summary.HasMoov && summary.Samples > 0, summary.Samples, (long)(summary.DurationSeconds * 1000), 0);
    }

    public Task<VideoJoinResult> JoinAsync(IReadOnlyList<VideoJoinPart> parts, string outputPath, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
        {
            throw new ArgumentException("No parts to join.", nameof(parts));
        }

        return Task.Run(
            () =>
            {
                var segments = parts.Select(p => new ComposeSegment(p.Path, p.StartMs * TimeSpan.TicksPerMillisecond)).ToList();
                var duration = EndTicks(parts[^1].Path) + segments[^1].StartTicks;
                try
                {
                    var copied = Mp4Composer.Copy(outputPath, segments, null, duration, progress, cancellationToken);
                    return new VideoJoinResult(copied.Frames, copied.DurationTicks / TimeSpan.TicksPerMillisecond, copied.Bytes, false);
                }
                catch (VideoCaptureException) when (!cancellationToken.IsCancellationRequested)
                {
                    // Parts from different encoders (a hardware encoder failed mid-recording): encode them again as one.
                    var (width, height, frameRate, bitrate) = Format(parts[0].Path);
                    var encoded = Mp4Composer.Encode(outputPath, segments, width, height, frameRate, bitrate, null, [], null, duration, progress, cancellationToken);
                    return new VideoJoinResult(encoded.Frames, encoded.DurationTicks / TimeSpan.TicksPerMillisecond, encoded.Bytes, true);
                }
            },
            cancellationToken);
    }

    public Task ComposeAsync(VideoComposeRequest request, IProgress<double>? progress, CancellationToken cancellationToken) =>
        Mp4Composer.ComposeAsync(request, progress, cancellationToken);

    private static long EndTicks(string path)
    {
        var summary = Mp4FileSummary.Read(path);
        return (long)(summary.DurationSeconds * TimeSpan.TicksPerSecond);
    }

    private static (int Width, int Height, int FrameRate, int Bitrate) Format(string path)
    {
        MediaFoundationRuntime.EnsureStarted();
        try
        {
            using var reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
            using var type = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
            var size = type.GetUInt64(MediaTypeAttributeKeys.FrameSize);
            var rate = type.GetUInt64(MediaTypeAttributeKeys.FrameRate);
            var numerator = (int)(rate >> 32);
            var denominator = Math.Max(1, (int)(rate & 0xFFFFFFFF));
            var width = (int)(size >> 32);
            var height = (int)(size & 0xFFFFFFFF);
            var frameRate = Math.Clamp((int)Math.Round((double)numerator / denominator), 1, 120);
            var bitrate = type.GetUInt32(MediaTypeAttributeKeys.AvgBitrate, out var avg).Success && avg > 0
                ? (int)avg
                : Encoding.VideoBitrate.For(width, height, frameRate, Encoding.VideoQuality.Balanced);
            return (width, height, frameRate, bitrate);
        }
        catch (SharpGenException ex)
        {
            throw new VideoCaptureException(VideoErrorCode.WriteFailed, $"A video part could not be read (0x{ex.HResult:X8}). The recorded files are kept unchanged.", ex);
        }
    }
}
