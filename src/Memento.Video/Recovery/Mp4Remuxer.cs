using Memento.Video.Gpu;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Memento.Video.Recovery;

/// <summary>
/// Copies the H.264 frames of one or more MP4 files into one ordinary (non-fragmented) MP4 without decoding them:
/// Media Foundation's source reader hands out the compressed samples and a sink writer in pass-through mode stores
/// them with a full sample index. Used to finalize a recorded track and to make a crashed one playable everywhere.
/// Parts are joined on the recording timeline: each part's samples are shifted by its start offset.
/// </summary>
public static class Mp4Remuxer
{
    /// <summary>One input file and where it starts on the output's timeline (100 ns).</summary>
    public sealed record Part(string Path, long StartTicks);

    /// <summary>Samples copied and the output's media time span.</summary>
    public sealed record Result(long Samples, long FirstTicks, long EndTicks, long OutputBytes);

    public static Task<Result> RemuxAsync(IReadOnlyList<Part> parts, string outputPath, CancellationToken cancellationToken) =>
        Task.Run(() => Remux(parts, outputPath, cancellationToken), cancellationToken);

    private static Result Remux(IReadOnlyList<Part> parts, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(parts);
        if (parts.Count == 0)
        {
            throw new ArgumentException("No parts to remux.", nameof(parts));
        }

        MediaFoundationRuntime.EnsureStarted();
        var temp = outputPath + ".tmp";
        IMFSinkWriter? writer = null;
        byte[]? sequenceHeader = null;
        var stream = 0;
        long samples = 0;
        long first = long.MaxValue;
        long end = 0;
        try
        {
            foreach (var part in parts)
            {
                using var reader = MediaFactory.MFCreateSourceReaderFromURL(part.Path, null);
                reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
                reader.SetStreamSelection(SourceReaderIndex.FirstVideoStream, true);
                using var type = reader.GetNativeMediaType(SourceReaderIndex.FirstVideoStream, 0);
                var header = TryBlob(type, MediaTypeAttributeKeys.MpegSequenceHeader);
                if (writer is null)
                {
                    using var attributes = MediaFactory.MFCreateAttributes(1);
                    attributes.Set(TranscodeAttributeKeys.TranscodeContainertype, TranscodeContainerTypeGuids.Mpeg4);
                    if (File.Exists(temp))
                    {
                        File.Delete(temp);
                    }

                    writer = MediaFactory.MFCreateSinkWriterFromURL(temp, null, attributes);
                    stream = writer.AddStream(type);
                    writer.SetInputMediaType(stream, type, null);
                    writer.BeginWriting();
                    sequenceHeader = header;
                }
                else if (!SameBytes(header, sequenceHeader))
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

                    var shifted = time + part.StartTicks;
                    sample.SampleTime = shifted;
                    writer.WriteSample(stream, sample);
                    samples++;
                    first = Math.Min(first, shifted);
                    end = Math.Max(end, shifted + sample.SampleDuration);
                }
            }

            writer!.Finalize();
            writer.Dispose();
            writer = null;
            File.Move(temp, outputPath, overwrite: true);
            return new Result(samples, first == long.MaxValue ? 0 : first, end, new FileInfo(outputPath).Length);
        }
        catch (SharpGenException ex)
        {
            throw new VideoCaptureException(VideoErrorCode.WriteFailed, $"The video file could not be rewritten (0x{ex.HResult:X8}). The recorded file is kept unchanged.", ex);
        }
        finally
        {
            writer?.Dispose();
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }
        }
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
}
