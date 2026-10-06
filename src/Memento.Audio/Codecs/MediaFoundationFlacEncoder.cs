using System.Globalization;
using Memento.Audio.Writing;
using Memento.Core.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.MediaFoundation;
using NAudio.Wave;

namespace Memento.Audio.Codecs;

/// <summary>
/// Encodes finished WAV track parts (read in sequence as one stream) to one FLAC file with the Windows FLAC MFT,
/// using the verified recipe (ENGINE-NOTES.md §A): activate the MFT, <c>SetInputType(PCM)</c>, take
/// <c>GetOutputAvailableType(0)</c>, build the sink-writer encoder from that type with the FLAC container.
/// <para>
/// The MF FLAC sink buffers the whole encode in <c>%TEMP%</c> and writes the output only when it finalizes, so the
/// encoder first checks that <c>%TEMP%</c> has 1.1 × the WAV size free. Output goes to <c>.tmp</c>, is verified
/// bit-exact, then moved into place; on any failure the WAV is untouched and the partial file is removed.
/// </para>
/// </summary>
public sealed partial class MediaFoundationFlacEncoder
{
    /// <summary>Free space required in the temp folder, as a multiple of the WAV data size.</summary>
    public const double TempSpaceFactor = 1.1;

    private readonly IFreeSpaceProbe _freeSpace;
    private readonly Func<string> _tempDirectory;
    private readonly ILogger _logger;

    public MediaFoundationFlacEncoder(IFreeSpaceProbe? freeSpace = null, ILogger<MediaFoundationFlacEncoder>? logger = null, Func<string>? tempDirectory = null)
    {
        _freeSpace = freeSpace ?? new DriveFreeSpaceProbe();
        _tempDirectory = tempDirectory ?? Path.GetTempPath;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <summary>Throws <see cref="AudioEncodeException"/> (<see cref="AudioEncodeErrorCode.InsufficientTempSpace"/>) if the temp folder is too full.</summary>
    public void CheckTempSpace(WavTrackSet input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var temp = _tempDirectory();
        var required = (long)Math.Ceiling(input.TotalDataBytes * TempSpaceFactor);
        var free = _freeSpace.GetFreeBytes(temp);
        if (free is null)
        {
            LogTempSpaceUnknown(_logger, temp);
            return;
        }

        if (free.Value < required)
        {
            var drive = Path.GetPathRoot(Path.GetFullPath(temp)) ?? temp;
            throw new AudioEncodeException(
                AudioEncodeErrorCode.InsufficientTempSpace,
                string.Create(
                    CultureInfo.InvariantCulture,
                    $"Not enough free space on {drive} for the FLAC encoder's temporary file: {TrackName(input)} needs {Bytes(required)}, {Bytes(free.Value)} is free. The WAV track is kept and nothing was lost. Free up {Bytes(required - free.Value)} on {drive} and finalize again."))
            {
                RequiredBytes = required,
                AvailableBytes = free.Value,
            };
        }
    }

    public Task<EncodeResult> EncodeAsync(WavTrackSet input, string outputPath, CancellationToken cancellationToken) =>
        EncodeAsync(input, outputPath, FlacEncodeOptions.Default, null, cancellationToken);

    public async Task<EncodeResult> EncodeAsync(WavTrackSet input, string outputPath, FlacEncodeOptions? options, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        options ??= FlacEncodeOptions.Default;
        var format = input.Format;
        if (format.IsFloat || format.BitsPerSample is not (8 or 16 or 24) || format.Channels > 8 || format.SampleRate is < 44_100 or > 192_000)
        {
            throw new AudioEncodeException(
                AudioEncodeErrorCode.UnsupportedInput,
                $"{TrackName(input)} is {format}; the Windows FLAC encoder takes 8/16/24-bit integer PCM, 1–8 channels, 44.1–192 kHz. The WAV track is kept.");
        }

        if (File.Exists(outputPath) && !options.Overwrite)
        {
            throw new AudioEncodeException(AudioEncodeErrorCode.OutputExists, $"{Path.GetFileName(outputPath)} already exists and finalized tracks are never overwritten. The WAV track is kept.");
        }

        CheckTempSpace(input);
        var temporary = outputPath + ".tmp";
        try
        {
            await Task.Run(() => Encode(input, temporary, progress, cancellationToken), cancellationToken).ConfigureAwait(false);
            if (options.VerifyBitExact)
            {
                await Task.Run(() => Verify(input, temporary, cancellationToken), cancellationToken).ConfigureAwait(false);
            }

            File.Move(temporary, outputPath, options.Overwrite);
        }
        catch (Exception ex) when (ex is not AudioEncodeException and not OperationCanceledException)
        {
            TryDelete(temporary);
            throw new AudioEncodeException(
                AudioEncodeErrorCode.EncodeFailed,
                $"Encoding {TrackName(input)} to FLAC failed (0x{ex.HResult:X8}: {ex.Message}). The WAV track is kept and nothing was lost; finalize again or keep the WAV.",
                ex);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }

        var bytes = new FileInfo(outputPath).Length;
        var sha = await FileHashing.Sha256Async(outputPath, cancellationToken).ConfigureAwait(false);
        var trackName = TrackName(input);
        LogEncoded(_logger, trackName, input.TotalDataBytes, bytes);
        return new EncodeResult(outputPath, bytes, sha, input.Duration);
    }

    private static void Encode(WavTrackSet input, string temporary, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        var flac = MediaFoundationRuntime.ActivateEncoder(MediaFoundationRuntime.FlacSubtype, "FLAC")
            ?? throw new AudioEncodeException(AudioEncodeErrorCode.EncoderUnavailable, $"The Windows FLAC encoder is not installed, so {TrackName(input)} stays as WAV. Install the Media Feature Pack (Windows N editions) and finalize again.");
        try
        {
            using var reader = input.OpenReader();
            var inputType = MediaFoundationApi.CreateMediaTypeFromWaveFormat(reader.WaveFormat);
            flac.Transform.SetInputType(0, inputType, 0);
            flac.Transform.GetOutputAvailableType(0, 0, out var outputType);
            using var encoder = new MediaFoundationEncoder(new MediaType(outputType));
            using var output = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            encoder.Encode(output, new CancellableWaveProvider(reader, input.TotalDataBytes, progress, cancellationToken), MediaFoundationRuntime.FlacContainer);
            output.Flush(true);
        }
        finally
        {
            MediaFoundationRuntime.Release(flac.Activate, flac.Transform);
        }
    }

    private static void Verify(WavTrackSet input, string encoded, CancellationToken cancellationToken)
    {
        using var source = input.OpenReader();
        using var decoded = new MediaFoundationReader(encoded);
        var df = decoded.WaveFormat;
        if (df.SampleRate != input.Format.SampleRate || df.Channels != input.Format.Channels || df.BitsPerSample != input.Format.BitsPerSample)
        {
            throw new AudioEncodeException(AudioEncodeErrorCode.EncodeFailed, $"The FLAC of {TrackName(input)} decodes as {df}, not {input.Format}. The WAV track is kept.");
        }

        var a = new byte[1 << 20];
        var b = new byte[a.Length];
        long position = 0;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var na = ReadFull(source, a);
            var nb = ReadFull(decoded, b);
            if (na != nb || !a.AsSpan(0, na).SequenceEqual(b.AsSpan(0, nb)))
            {
                throw new AudioEncodeException(
                    AudioEncodeErrorCode.EncodeFailed,
                    string.Create(CultureInfo.InvariantCulture, $"The FLAC of {TrackName(input)} does not match the WAV after {position / input.Format.BytesPerSecond} s. The WAV track is kept; finalize again."));
            }

            if (na == 0)
            {
                return;
            }

            position += na;
        }
    }

    private static int ReadFull(IWaveProvider provider, byte[] buffer)
    {
        var total = 0;
        int n;
        while (total < buffer.Length && (n = provider.Read(buffer, total, buffer.Length - total)) > 0)
        {
            total += n;
        }

        return total;
    }

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

    internal static string TrackName(WavTrackSet input) => Path.GetFileName(input.Parts[0].Path);

    internal static string Bytes(long bytes) => bytes switch
    {
        >= 1L << 30 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 30):0.0} GB"),
        >= 1L << 20 => string.Create(CultureInfo.InvariantCulture, $"{bytes / (double)(1L << 20):0.0} MB"),
        _ => string.Create(CultureInfo.InvariantCulture, $"{bytes / 1024.0:0} KB"),
    };

    [LoggerMessage(Level = LogLevel.Information, Message = "FLAC encoded {Track}: {WavBytes} WAV bytes → {FlacBytes} bytes")]
    private static partial void LogEncoded(ILogger logger, string track, long wavBytes, long flacBytes);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Free space in the temp folder {TempFolder} is unknown; encoding without the pre-check")]
    private static partial void LogTempSpaceUnknown(ILogger logger, string tempFolder);
}
