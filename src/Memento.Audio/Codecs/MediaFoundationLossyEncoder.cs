using System.Runtime.InteropServices;
using Memento.Audio.Mixing;
using Memento.Audio.Writing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.MediaFoundation;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Memento.Audio.Codecs;

/// <summary>
/// MP3 and AAC (.m4a) with the Windows Media Foundation encoders, from a recorded track or any decodable file,
/// with optional mono downmix. Input is converted to 16-bit PCM at 44.1/48 kHz (what both encoders take).
/// Lossy files are never timeline-exact (+37 ms MP3, +11 ms AAC of priming/padding; ENGINE-NOTES.md §A), so
/// transcripts always come from the lossless track.
/// </summary>
public sealed partial class MediaFoundationLossyEncoder(ILogger<MediaFoundationLossyEncoder>? logger = null)
{
    private readonly ILogger _logger = (ILogger?)logger ?? NullLogger.Instance;

    public Task<EncodeResult> EncodeAsync(WavTrackSet input, string outputPath, LossyEncodeOptions options, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        return EncodeAsync(() => MediaFoundationDecoder.Open(input), Path.GetFileName(input.Parts[0].Path), outputPath, options, progress, cancellationToken);
    }

    public Task<EncodeResult> EncodeAsync(string inputPath, string outputPath, LossyEncodeOptions options, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(inputPath);
        return EncodeAsync(() => MediaFoundationDecoder.Open(inputPath), Path.GetFileName(inputPath), outputPath, options, progress, cancellationToken);
    }

    private async Task<EncodeResult> EncodeAsync(Func<DecodedAudio> open, string inputName, string outputPath, LossyEncodeOptions options, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        ArgumentNullException.ThrowIfNull(options);
        var (min, max) = options.Codec == LossyCodec.Mp3 ? (96, 320) : (16, 320);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BitrateKbps, min, nameof(options));
        ArgumentOutOfRangeException.ThrowIfGreaterThan(options.BitrateKbps, max, nameof(options));
        if (File.Exists(outputPath) && !options.Overwrite)
        {
            throw new AudioEncodeException(AudioEncodeErrorCode.OutputExists, $"{Path.GetFileName(outputPath)} already exists; nothing was changed. Choose another name or remove it first.");
        }

        var temporary = outputPath + ".tmp";
        (TimeSpan Duration, int Kbps) encoded;
        try
        {
            encoded = await Task.Run(() => Encode(open, inputName, temporary, options, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
            File.Move(temporary, outputPath, options.Overwrite);
        }
        catch (Exception ex) when (ex is not AudioEncodeException and not OperationCanceledException and not ArgumentException)
        {
            TryDelete(temporary);
            throw new AudioEncodeException(
                AudioEncodeErrorCode.EncodeFailed,
                $"Encoding {inputName} to {options.Codec.ToString().ToUpperInvariant()} failed (0x{ex.HResult:X8}: {ex.Message}). The original is unchanged.",
                ex);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        var bytes = new FileInfo(outputPath).Length;
        var sha = await FileHashing.Sha256Async(outputPath, cancellationToken).ConfigureAwait(false);
        LogEncoded(_logger, inputName, options.Codec, encoded.Kbps, bytes);
        return new EncodeResult(outputPath, bytes, sha, encoded.Duration, encoded.Kbps);
    }

    private static (TimeSpan Duration, int Kbps) Encode(Func<DecodedAudio> open, string inputName, string temporary, LossyEncodeOptions options, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        MediaFoundationRuntime.EnsureStarted();
        using var audio = open();
        ISampleProvider samples = audio;
        if (options.DownmixToMono && audio.Channels > 1)
        {
            samples = new ChannelMapSampleProvider(samples, 1);
        }
        else if (audio.Channels > 2)
        {
            samples = new ChannelMapSampleProvider(samples, 2);
        }

        if (samples.WaveFormat.SampleRate is not (44_100 or 48_000))
        {
            samples = new WdlResamplingSampleProvider(samples, 48_000);
        }

        var pcm16 = new SampleToWaveProvider16(samples);
        var subtype = options.Codec == LossyCodec.Mp3 ? AudioSubtypes.MFAudioFormat_MP3 : AudioSubtypes.MFAudioFormat_AAC;
        MediaType? mediaType;
        try
        {
            mediaType = MediaFoundationEncoder.SelectMediaType(subtype, pcm16.WaveFormat, options.BitrateKbps * 1000);
        }
        catch (COMException ex)
        {
            throw Unavailable(options, pcm16.WaveFormat, inputName, ex);
        }

        if (mediaType is null)
        {
            throw Unavailable(options, pcm16.WaveFormat, inputName, null);
        }

        var kbps = mediaType.AverageBytesPerSecond * 8 / 1000; // read before the encoder releases the type
        var container = options.Codec == LossyCodec.Mp3 ? TranscodeContainerTypes.MFTranscodeContainerType_MP3 : TranscodeContainerTypes.MFTranscodeContainerType_MPEG4;
        var expectedBytes = audio.Duration is { } d ? (long)(d.TotalSeconds * pcm16.WaveFormat.AverageBytesPerSecond) : 0;
        var counting = new CancellableWaveProvider(pcm16, expectedBytes, progress, cancellationToken);
        using (var encoder = new MediaFoundationEncoder(mediaType))
        using (var output = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            encoder.Encode(output, counting, container);
            output.Flush(true);
        }

        var frames = counting.BytesRead / pcm16.WaveFormat.BlockAlign;
        var duration = TimeSpan.FromTicks(QpcClock.FramesToTicks(frames, pcm16.WaveFormat.SampleRate));
        return (duration, kbps);
    }

    private static AudioEncodeException Unavailable(LossyEncodeOptions options, WaveFormat format, string inputName, Exception? inner) =>
        new(
            AudioEncodeErrorCode.EncoderUnavailable,
            $"Windows has no {options.Codec.ToString().ToUpperInvariant()} encoder setting for {format.SampleRate} Hz, {format.Channels} ch near {options.BitrateKbps} kbit/s, so {inputName} was not converted and is unchanged. Choose another bitrate or keep the lossless file.",
            inner);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Encoded {Input} to {Codec} at {Kbps} kbit/s: {Bytes} bytes")]
    private static partial void LogEncoded(ILogger logger, string input, LossyCodec codec, int kbps, long bytes);
}
