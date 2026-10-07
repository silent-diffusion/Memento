using System.Globalization;
using System.Runtime.InteropServices;
using Memento.Audio.Codecs;
using Memento.Audio.Writing;
using Memento.Core.Import;
using NAudio.MediaFoundation;

namespace Memento.Audio.Adapters;

/// <summary>
/// Core's <see cref="IMediaDecoder"/> with Media Foundation: any file Windows plays (WAV, FLAC, MP3, M4A/AAC, WMA,
/// and Ogg/Opus where the codec is installed; video containers give their first audio stream). Decodes to 24-bit PCM
/// at the file's own rate and channel count through <see cref="RollingWavWriter"/>, so a long file splits at 3.5 GiB
/// like a recording.
/// </summary>
public sealed class MediaFoundationMediaDecoder : IMediaDecoder
{
    /// <summary><c>MF_SOURCE_READER_FIRST_VIDEO_STREAM</c>.</summary>
    private const int FirstVideoStream = unchecked((int)0xFFFFFFFC);

    private const int BlockFrames = 16_384;

    public Task<MediaProbe> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(
            () =>
            {
                try
                {
                    using var audio = MediaFoundationDecoder.Open(path);
                    var duration = audio.Duration is { } d ? (long)d.TotalMilliseconds : 0;
                    return new MediaProbe(audio.SampleRate, audio.Channels, duration, HasVideoStream(path));
                }
                catch (Exception ex) when (IsDecodeFailure(ex))
                {
                    throw new InvalidDataException(Describe(ex), ex);
                }
            },
            cancellationToken);
    }

    public Task<DecodedWav> DecodeToWavAsync(string path, string directory, string stem, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() => Decode(path, directory, stem, progress, cancellationToken), cancellationToken);
    }

    /// <summary>Whether the file has a video stream (only its audio is imported).</summary>
    public static bool HasVideoStream(string path)
    {
        if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        MediaFoundationRuntime.EnsureStarted();
        IMFSourceReader? reader = null;
        try
        {
            MediaFoundationInterop.MFCreateSourceReaderFromURL(path, null, out reader);
            reader.GetNativeMediaType(FirstVideoStream, 0, out var type);
            Marshal.ReleaseComObject(type);
            return true;
        }
        catch (COMException)
        {
            return false;
        }
        finally
        {
            if (reader is not null)
            {
                Marshal.ReleaseComObject(reader);
            }
        }
    }

    private static DecodedWav Decode(string path, string directory, string stem, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        DecodedAudio audio;
        try
        {
            audio = MediaFoundationDecoder.Open(path);
        }
        catch (Exception ex) when (IsDecodeFailure(ex))
        {
            throw new InvalidDataException(Describe(ex), ex);
        }

        using (audio)
        {
            var format = AudioFormat.Pcm24(audio.SampleRate, audio.Channels);
            var expectedFrames = audio.Duration is { } duration ? (long)(duration.TotalSeconds * audio.SampleRate) : 0;
            var samples = new float[BlockFrames * audio.Channels];
            var bytes = new byte[samples.Length * 3];
            long frames = 0;
            var writer = new RollingWavWriter(directory, stem, format);
            try
            {
                int read;
                while ((read = ReadBlock(audio, samples)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var whole = read - (read % audio.Channels);
                    PcmConverter.FloatToInt24(samples.AsSpan(0, whole), bytes);
                    writer.Write(bytes.AsSpan(0, whole * 3));
                    frames += whole / audio.Channels;
                    if (expectedFrames > 0)
                    {
                        progress?.Report(Math.Min(1.0, (double)frames / expectedFrames));
                    }
                }

                writer.Checkpoint();
                writer.Dispose();
                progress?.Report(1.0);
                return new DecodedWav(writer.Parts.ToList(), format.SampleRate, format.Channels, frames);
            }
            catch (Exception ex)
            {
                writer.Dispose();
                foreach (var part in writer.Parts)
                {
                    TryDelete(part);
                }

                if (IsDecodeFailure(ex))
                {
                    throw new InvalidDataException(Describe(ex), ex);
                }

                throw;
            }
        }
    }

    /// <summary>Fills the block (Media Foundation may return less than asked before the end).</summary>
    private static int ReadBlock(DecodedAudio audio, float[] samples)
    {
        var total = 0;
        while (total < samples.Length)
        {
            var read = audio.Read(samples, total, samples.Length - total);
            if (read == 0)
            {
                break;
            }

            total += read;
        }

        return total;
    }

    private static bool IsDecodeFailure(Exception ex) =>
        ex is COMException or InvalidOperationException or ArgumentException or NotSupportedException or InvalidDataException or FormatException;

    private static string Describe(Exception ex) => ex.HResult switch
    {
        unchecked((int)0xC00D36C4) => "the file type is not supported",
        unchecked((int)0xC00D36B4) or unchecked((int)0xC00D5212) => "no decoder for its audio is installed in Windows",
        unchecked((int)0xC00D36E6) => "it has no audio stream",
        _ when ex is InvalidDataException => ex.Message,
        _ => string.Create(CultureInfo.InvariantCulture, $"0x{ex.HResult:X8}: {ex.Message.TrimEnd('.')}"),
    };

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The project that held it is removed as a whole.
        }
    }
}
