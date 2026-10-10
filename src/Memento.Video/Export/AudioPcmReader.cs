using Memento.Video.Gpu;
using SharpGen.Runtime;
using Vortice.MediaFoundation;

namespace Memento.Video.Export;

/// <summary>
/// Decodes the recording's mix (FLAC, or AAC or MP3 after "smaller files") to 16-bit PCM for an export's AAC track, in
/// the samples Media Foundation hands out, each with its time from the start of the recording.
/// </summary>
internal sealed class AudioPcmReader : IDisposable
{
    private readonly IMFSourceReader _reader;
    private bool _ended;

    public AudioPcmReader(string path)
    {
        MediaFoundationRuntime.EnsureStarted();
        try
        {
            _reader = MediaFactory.MFCreateSourceReaderFromURL(path, null);
            _reader.SetStreamSelection(SourceReaderIndex.AllStreams, false);
            _reader.SetStreamSelection(SourceReaderIndex.FirstAudioStream, true);
            using var native = _reader.GetNativeMediaType(SourceReaderIndex.FirstAudioStream, 0);
            var rate = (int)native.GetUInt32(MediaTypeAttributeKeys.AudioSamplesPerSecond);
            var channels = (int)native.GetUInt32(MediaTypeAttributeKeys.AudioNumChannels);
            SampleRate = rate is 44_100 or 48_000 ? rate : 48_000;
            Channels = channels is 1 or 2 ? channels : 2;
            using var pcm = MediaFactory.MFCreateMediaType();
            pcm.Set(MediaTypeAttributeKeys.MajorType, MediaTypeGuids.Audio);
            pcm.Set(MediaTypeAttributeKeys.Subtype, AudioFormatGuids.Pcm);
            pcm.Set(MediaTypeAttributeKeys.AudioBitsPerSample, 16u);
            pcm.Set(MediaTypeAttributeKeys.AudioSamplesPerSecond, (uint)SampleRate);
            pcm.Set(MediaTypeAttributeKeys.AudioNumChannels, (uint)Channels);
            pcm.Set(MediaTypeAttributeKeys.AudioBlockAlignment, (uint)(Channels * 2));
            pcm.Set(MediaTypeAttributeKeys.AudioAvgBytesPerSecond, (uint)(Channels * 2 * SampleRate));
            _reader.SetCurrentMediaType(SourceReaderIndex.FirstAudioStream, pcm);
        }
        catch (SharpGenException ex)
        {
            throw new VideoCaptureException(VideoErrorCode.WriteFailed, $"The recording's mix could not be read for the video export (0x{ex.HResult:X8}). Nothing inside Memento was changed.", ex);
        }
    }

    public int SampleRate { get; }

    public int Channels { get; }

    /// <summary>The time just after the last sample read (100 ns).</summary>
    public long Position { get; private set; }

    /// <summary>The next block of PCM, or null at the end. The caller disposes it.</summary>
    public IMFSample? Next()
    {
        while (!_ended)
        {
            var sample = _reader.ReadSample(SourceReaderIndex.FirstAudioStream, SourceReaderControlFlag.None, out _, out var flags, out var time);
            if ((flags & SourceReaderFlag.EndOfStream) != 0)
            {
                _ended = true;
            }

            if (sample is null)
            {
                continue;
            }

            Position = Math.Max(Position, time + sample.SampleDuration);
            return sample;
        }

        return null;
    }

    public void Dispose() => _reader.Dispose();
}
