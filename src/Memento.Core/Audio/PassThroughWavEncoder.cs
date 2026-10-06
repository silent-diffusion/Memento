using System.Buffers.Binary;

namespace Memento.Core.Audio;

/// <summary>
/// Keeps WAV. Copies the file unchanged, or writes a one-channel copy when downmixing.
/// The fallback when no encoder for the configured codec is installed.
/// </summary>
public sealed class PassThroughWavEncoder : IAudioEncoder
{
    public const string WavCodec = "wav";

    private const int BlockFrames = 4096;

    public string Codec => WavCodec;

    public string FileExtension => ".wav";

    public bool IsLossless => true;

    public async Task EncodeAsync(string sourceWavPath, string destinationPath, AudioEncodeOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        var sameFile = string.Equals(Path.GetFullPath(sourceWavPath), Path.GetFullPath(destinationPath), StringComparison.OrdinalIgnoreCase);
        var info = WavInfo.Read(sourceWavPath);
        if (!options.DownmixMono || info.Format.Channels == 1)
        {
            if (!sameFile)
            {
                await CopyAsync(sourceWavPath, destinationPath, cancellationToken);
            }

            return;
        }

        var temporary = destinationPath + ".tmp";
        await Task.Run(() => WriteMono(sourceWavPath, temporary, cancellationToken), cancellationToken);
        File.Move(temporary, destinationPath, overwrite: true);
    }

    private static async Task CopyAsync(string source, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination + ".tmp";
        await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, useAsync: true))
        await using (var output = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024, useAsync: true))
        {
            await input.CopyToAsync(output, cancellationToken);
            await output.FlushAsync(cancellationToken);
            output.Flush(flushToDisk: true);
        }

        File.Move(temporary, destination, overwrite: true);
    }

    private static void WriteMono(string source, string destination, CancellationToken cancellationToken)
    {
        using var reader = new WavReader(source);
        var inFormat = reader.Format;
        var outFormat = inFormat.Encoding == SampleEncoding.IeeeFloat || inFormat.BitsPerSample > 16
            ? PcmFormat.IeeeFloat32(inFormat.SampleRate, 1)
            : PcmFormat.Pcm16(inFormat.SampleRate, 1);
        if (File.Exists(destination))
        {
            File.Delete(destination);
        }

        using var writer = new StreamingWavWriter(destination, outFormat);
        var frames = new float[BlockFrames * inFormat.Channels];
        var bytes = new byte[BlockFrames * outFormat.BlockAlign];
        int read;
        while ((read = reader.ReadFrames(frames)) > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            for (var f = 0; f < read; f++)
            {
                var sum = 0f;
                for (var c = 0; c < inFormat.Channels; c++)
                {
                    sum += frames[(f * inFormat.Channels) + c];
                }

                var mono = sum / inFormat.Channels;
                if (outFormat.Encoding == SampleEncoding.IeeeFloat)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(f * 4), mono);
                }
                else
                {
                    BinaryPrimitives.WriteInt16LittleEndian(bytes.AsSpan(f * 2), (short)Math.Clamp(MathF.Round(mono * short.MaxValue), short.MinValue, short.MaxValue));
                }
            }

            writer.Write(bytes.AsSpan(0, read * outFormat.BlockAlign));
        }
    }
}
