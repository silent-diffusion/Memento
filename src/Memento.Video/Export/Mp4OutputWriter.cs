using System.Runtime.InteropServices;
using Memento.Video.Encoding;
using Memento.Video.Gpu;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Memento.Video.Export;

/// <summary>
/// An ordinary (not fragmented) MP4 written by Media Foundation's sink writer: an H.264 picture track, either encoded
/// here from NV12 pictures or copied from a recorded file's samples untouched, and optionally an AAC track encoded
/// from PCM. The file is written to <c>&lt;path&gt;.tmp</c> and moved into place by <see cref="Finish"/>.
/// </summary>
internal sealed class Mp4OutputWriter : IDisposable
{
    private readonly IMFSinkWriter _writer;
    private readonly string _path;
    private readonly string _temp;
    private readonly int _video;
    private readonly int? _audio;
    private readonly int _frameBytes;
    private bool _finished;

    private Mp4OutputWriter(IMFSinkWriter writer, string path, string temp, int video, int? audio, int frameBytes, bool hardware)
    {
        _writer = writer;
        _path = path;
        _temp = temp;
        _video = video;
        _audio = audio;
        _frameBytes = frameBytes;
        Hardware = hardware;
    }

    /// <summary>The graphics card's encoder took the picture (false: Microsoft's software encoder, or a copy).</summary>
    public bool Hardware { get; }

    /// <summary>Opens a writer that encodes NV12 pictures; the graphics card's encoder first when <paramref name="preferHardware"/>, else software.</summary>
    public static Mp4OutputWriter Encoding(string path, int width, int height, int frameRate, int bitsPerSecond, AudioPcmReader? audio, bool preferHardware)
    {
        if (preferHardware)
        {
            try
            {
                return Open(path, audio, hardware: true, writer => AddEncodedVideo(writer, width, height, frameRate, bitsPerSecond), width * height * 3 / 2);
            }
            catch (VideoCaptureException)
            {
                // No hardware encoder takes system-memory pictures here: software below.
            }
        }

        return Open(path, audio, hardware: false, writer => AddEncodedVideo(writer, width, height, frameRate, bitsPerSecond), width * height * 3 / 2);
    }

    /// <summary>Opens a writer that copies H.264 samples of <paramref name="nativeType"/> untouched.</summary>
    public static Mp4OutputWriter Copying(string path, IMFMediaType nativeType, AudioPcmReader? audio) =>
        Open(path, audio, hardware: false, writer =>
        {
            var stream = writer.AddStream(nativeType);
            writer.SetInputMediaType(stream, nativeType, null);
            return stream;
        }, 0);

    public void WriteVideo(Nv12Frame frame, long time, long duration)
    {
        using var buffer = MediaFactory.MFCreateMemoryBuffer(_frameBytes);
        buffer.Lock(out var pointer, out _, out _);
        try
        {
            Marshal.Copy(frame.Data, 0, pointer, _frameBytes);
        }
        finally
        {
            buffer.Unlock();
        }

        buffer.CurrentLength = _frameBytes;
        using var sample = MediaFactory.MFCreateSample();
        sample.AddBuffer(buffer);
        sample.SampleTime = time;
        sample.SampleDuration = duration;
        Write(_video, sample);
    }

    public void WriteVideoSample(IMFSample sample) => Write(_video, sample);

    public void WriteAudio(IMFSample sample)
    {
        if (_audio is { } stream)
        {
            Write(stream, sample);
        }
    }

    /// <summary>Finishes the file and moves it into place.</summary>
    public void Finish()
    {
        try
        {
            _writer.Finalize();
        }
        catch (SharpGenException ex)
        {
            throw new VideoCaptureException(VideoErrorCode.WriteFailed, $"The video file could not be finished (0x{ex.HResult:X8}).", ex);
        }

        _writer.Dispose();
        _finished = true;
        File.Move(_temp, _path, overwrite: true);
    }

    public void Dispose()
    {
        if (!_finished)
        {
            _writer.Dispose();
            _finished = true;
            try
            {
                File.Delete(_temp);
            }
            catch (IOException)
            {
                // A half-written temporary file; the export removes its work folder anyway.
            }
        }
    }

    private static Mp4OutputWriter Open(string path, AudioPcmReader? audio, bool hardware, Func<IMFSinkWriter, int> addVideo, int frameBytes)
    {
        MediaFoundationRuntime.EnsureStarted();
        var temp = path + ".tmp";
        IMFSinkWriter? writer = null;
        try
        {
            if (File.Exists(temp))
            {
                File.Delete(temp);
            }

            using var attributes = MediaFactory.MFCreateAttributes(2);
            attributes.Set(TranscodeAttributeKeys.TranscodeContainertype, TranscodeContainerTypeGuids.Mpeg4);
            attributes.Set(SinkWriterAttributeKeys.ReadwriteEnableHardwareTransforms, hardware ? 1u : 0u);
            writer = MediaFactory.MFCreateSinkWriterFromURL(temp, null, attributes);
            var video = addVideo(writer);
            int? audioStream = audio is null ? null : AddAudio(writer, audio);
            writer.BeginWriting();
            var result = new Mp4OutputWriter(writer, path, temp, video, audioStream, frameBytes, hardware);
            writer = null;
            return result;
        }
        catch (SharpGenException ex)
        {
            throw new VideoCaptureException(VideoErrorCode.EncoderUnavailable, $"The video export could not open an encoder (0x{ex.HResult:X8}). Nothing inside Memento was changed.", ex);
        }
        catch (COMException ex)
        {
            throw new VideoCaptureException(VideoErrorCode.EncoderUnavailable, $"The video export could not open an encoder (0x{ex.HResult:X8}). Nothing inside Memento was changed.", ex);
        }
        finally
        {
            if (writer is not null)
            {
                writer.Dispose();
                try
                {
                    File.Delete(temp);
                }
                catch (IOException)
                {
                    // Removed with the export's work folder.
                }
            }
        }
    }

    private static int AddEncodedVideo(IMFSinkWriter writer, int width, int height, int frameRate, int bitsPerSecond)
    {
        using var output = MediaFactory.MFCreateMediaType();
        output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        output.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.H264);
        output.Set(MediaTypeAttributeKeys.AvgBitrate, (uint)bitsPerSecond);
        output.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
        output.Set(MediaTypeAttributeKeys.FrameSize, MediaFactory.PackSize((uint)width, (uint)height));
        output.Set(MediaTypeAttributeKeys.FrameRate, MediaFactory.PackRatio(frameRate, 1));
        output.Set(MediaTypeAttributeKeys.PixelAspectRatio, MediaFactory.PackRatio(1, 1));
        output.Set(MediaTypeAttributeKeys.Mpeg2Profile, MediaFoundationGuids.H264ProfileHigh);
        var stream = writer.AddStream(output);

        using var input = MediaFactory.MFCreateMediaType();
        input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Video);
        input.Set(MediaTypeAttributeKeys.Subtype, VideoFormatGuids.NV12);
        input.Set(MediaTypeAttributeKeys.InterlaceMode, (uint)VideoInterlaceMode.Progressive);
        input.Set(MediaTypeAttributeKeys.FrameSize, MediaFactory.PackSize((uint)width, (uint)height));
        input.Set(MediaTypeAttributeKeys.FrameRate, MediaFactory.PackRatio(frameRate, 1));
        input.Set(MediaTypeAttributeKeys.PixelAspectRatio, MediaFactory.PackRatio(1, 1));
        input.Set(MediaTypeAttributeKeys.DefaultStride, (uint)width);

        using var encoding = MediaFactory.MFCreateAttributes(5);
        encoding.Set(MediaFoundationGuids.CodecApiRateControlMode, MediaFoundationGuids.RateControlPeakConstrainedVbr);
        encoding.Set(MediaFoundationGuids.CodecApiMeanBitRate, (uint)bitsPerSecond);
        encoding.Set(MediaFoundationGuids.CodecApiMaxBitRate, (uint)Math.Min(int.MaxValue, bitsPerSecond * 2L));
        encoding.Set(MediaFoundationGuids.CodecApiGopSize, (uint)(frameRate * 2));
        encoding.Set(MediaFoundationGuids.CodecApiBPictureCount, 0u);
        writer.SetInputMediaType(stream, input, encoding);
        return stream;
    }

    private static int AddAudio(IMFSinkWriter writer, AudioPcmReader audio)
    {
        using var output = MediaFactory.MFCreateMediaType();
        output.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        output.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Aac);
        output.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
        output.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)audio.SampleRate);
        output.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)audio.Channels);
        output.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, 24_000u);
        var stream = writer.AddStream(output);

        using var input = MediaFactory.MFCreateMediaType();
        input.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
        input.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
        input.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
        input.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)audio.SampleRate);
        input.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)audio.Channels);
        input.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)(audio.Channels * 2));
        input.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(audio.Channels * 2 * audio.SampleRate));
        writer.SetInputMediaType(stream, input, null);
        return stream;
    }

    private void Write(int stream, IMFSample sample)
    {
        try
        {
            _writer.WriteSample(stream, sample);
        }
        catch (SharpGenException ex)
        {
            throw new VideoCaptureException(VideoErrorCode.WriteFailed, $"The video file could not be written (0x{ex.HResult:X8}).", ex);
        }
    }
}
