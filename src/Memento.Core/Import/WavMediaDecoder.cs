using Memento.Core.Audio;

namespace Memento.Core.Import;

/// <summary>
/// The <see cref="IMediaDecoder"/> without Media Foundation: reads PCM and float WAV files only (tests, tools).
/// Memento.Audio replaces it with the Media Foundation decoder.
/// </summary>
public sealed class WavMediaDecoder : IMediaDecoder
{
    private const int BlockFrames = 8192;

    public Task<MediaProbe> ProbeAsync(string path, CancellationToken cancellationToken)
    {
        var info = Read(path);
        return Task.FromResult(new MediaProbe(info.Format.SampleRate, info.Format.Channels, info.DurationMs, HasVideo: false));
    }

    public Task<DecodedWav> DecodeToWavAsync(string path, string directory, string stem, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        Read(path);
        return Task.Run(
            () =>
            {
                Directory.CreateDirectory(directory);
                var output = Path.Combine(directory, stem + ".wav");
                using var reader = new WavReader(path);
                var format = new PcmFormat(reader.Format.SampleRate, reader.Format.Channels, 24, SampleEncoding.Pcm);
                using var writer = new StreamingWavWriter(output, format);
                var samples = new float[BlockFrames * format.Channels];
                var bytes = new byte[samples.Length * 3];
                long frames = 0;
                int read;
                while ((read = reader.ReadFrames(samples)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var count = read * format.Channels;
                    for (var i = 0; i < count; i++)
                    {
                        var value = (int)Math.Clamp(MathF.Round(samples[i] * 8_388_607f), -8_388_608f, 8_388_607f);
                        bytes[i * 3] = (byte)value;
                        bytes[(i * 3) + 1] = (byte)(value >> 8);
                        bytes[(i * 3) + 2] = (byte)(value >> 16);
                    }

                    writer.Write(bytes.AsSpan(0, count * 3));
                    frames += read;
                    progress?.Report(reader.TotalFrames == 0 ? 1 : (double)frames / reader.TotalFrames);
                }

                writer.Checkpoint();
                return new DecodedWav([output], format.SampleRate, format.Channels, frames);
            },
            cancellationToken);
    }

    private static WavInfo Read(string path)
    {
        try
        {
            return WavInfo.Read(path);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or EndOfStreamException)
        {
            throw new InvalidDataException($"it is not a WAV file this build can read ({ex.Message.TrimEnd('.')})", ex);
        }
    }
}
