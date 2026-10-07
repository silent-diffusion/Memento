using Memento.Core.Audio;
using Memento.Core.Import;
using Memento.Core.Settings;

namespace Memento.Core.Export;

/// <summary>
/// Audio for an export, from the stored file (FLAC, or WAV/AAC/MP3 after optimisation): copied when the format
/// already matches, decoded to 24-bit WAV, or encoded to FLAC or MP3 with the Media Foundation encoders.
/// </summary>
public sealed class ExportAudio(IEnumerable<IAudioEncoder> encoders, IMediaDecoder decoder)
{
    private readonly IReadOnlyList<IAudioEncoder> _encoders = encoders.ToList();

    /// <summary>Whether the stored file is written as it is (no conversion).</summary>
    public static bool IsCopy(string sourceCodec, Bridge.Contracts.ExportAudioChoice choice) =>
        choice.Format switch
        {
            ExportRules.Flac => sourceCodec == StorageSettings.Flac,
            ExportRules.Wav => sourceCodec == PassThroughWavEncoder.WavCodec,
            _ => sourceCodec == StorageSettings.Mp3 && choice.BitrateKbps is null,
        };

    /// <summary>Size of the exported file: exact for a copy, otherwise from the duration and format.</summary>
    public static long Estimate(string sourceCodec, long sourceBytes, int sampleRate, int channels, long durationMs, Bridge.Contracts.ExportAudioChoice choice)
    {
        if (IsCopy(sourceCodec, choice))
        {
            return sourceBytes;
        }

        var pcmBytes = durationMs * Math.Max(1, sampleRate) / 1000 * Math.Max(1, channels) * 3;
        return choice.Format switch
        {
            ExportRules.Flac => (long)(pcmBytes * 0.6),
            ExportRules.Wav => 44 + pcmBytes,
            _ => durationMs * ExportRules.Mp3Bitrate(choice) / 8,
        };
    }

    /// <summary>Writes <paramref name="destination"/> from the stored <paramref name="source"/>; <paramref name="scratch"/> holds intermediate WAV files.</summary>
    public async Task WriteAsync(string source, string sourceCodec, Bridge.Contracts.ExportAudioChoice choice, string destination, string scratch, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(choice);
        if (IsCopy(sourceCodec, choice))
        {
            await CopyAsync(source, destination, cancellationToken);
            return;
        }

        switch (choice.Format)
        {
            case ExportRules.Wav:
                await DecodeAsync(source, destination, scratch, cancellationToken);
                break;
            case ExportRules.Flac:
                var flac = Encoder(StorageSettings.Flac);
                if (sourceCodec == PassThroughWavEncoder.WavCodec)
                {
                    await flac.EncodeAsync(source, destination, new AudioEncodeOptions(null, false), cancellationToken);
                    break;
                }

                var wav = Path.Combine(scratch, Path.GetFileNameWithoutExtension(destination) + ".decoded.wav");
                await DecodeAsync(source, wav, scratch, cancellationToken);
                try
                {
                    await flac.EncodeAsync(wav, destination, new AudioEncodeOptions(null, false), cancellationToken);
                }
                finally
                {
                    File.Delete(wav);
                }

                break;
            default:
                await Encoder(StorageSettings.Mp3).EncodeAsync(source, destination, new AudioEncodeOptions(ExportRules.Mp3Bitrate(choice), false), cancellationToken);
                break;
        }
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        await using var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, useAsync: true);
        await using var output = new FileStream(destination, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, useAsync: true);
        await input.CopyToAsync(output, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private IAudioEncoder Encoder(string codec) =>
        _encoders.FirstOrDefault(e => e.Codec == codec)
        ?? throw new IOException($"this build of Memento has no {codec.ToUpperInvariant()} encoder");

    private async Task DecodeAsync(string source, string destination, string scratch, CancellationToken cancellationToken)
    {
        var stem = Path.GetFileNameWithoutExtension(destination) + ".part";
        DecodedWav decoded;
        try
        {
            decoded = await decoder.DecodeToWavAsync(source, scratch, stem, null, cancellationToken);
        }
        catch (InvalidDataException ex)
        {
            throw new IOException($"the stored audio could not be decoded ({ex.Message.TrimEnd('.')})", ex);
        }

        if (decoded.Parts.Count != 1)
        {
            foreach (var part in decoded.Parts)
            {
                File.Delete(part);
            }

            throw new IOException("the audio is longer than one WAV file can hold (4 GB); choose FLAC instead");
        }

        File.Move(decoded.Parts[0], destination, overwrite: true);
    }
}
