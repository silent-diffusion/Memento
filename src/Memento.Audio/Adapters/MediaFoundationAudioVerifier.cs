using System.Runtime.InteropServices;
using Memento.Audio.Codecs;
using Memento.Core.Audio;

namespace Memento.Audio.Adapters;

/// <summary>
/// <see cref="IAudioFileVerifier"/> with Media Foundation: opens the file as Windows would play it and reads every
/// sample, so a file that only starts correctly does not pass.
/// </summary>
public sealed class MediaFoundationAudioVerifier : IAudioFileVerifier
{
    private const int BlockSamples = 1 << 16;

    public Task<AudioFileCheck> VerifyAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        return Task.Run(() => Verify(path, cancellationToken), cancellationToken);
    }

    private static AudioFileCheck Verify(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var audio = MediaFoundationDecoder.Open(path);
            var buffer = new float[BlockSamples];
            long samples = 0;
            int read;
            while ((read = audio.Read(buffer, 0, buffer.Length)) > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                samples += read;
            }

            var frames = samples / Math.Max(1, audio.Channels);
            return new AudioFileCheck(audio.SampleRate, audio.Channels, frames * 1000 / audio.SampleRate);
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException or IOException or ArgumentException or NotSupportedException)
        {
            throw new InvalidDataException($"{Path.GetFileName(path)} does not decode (0x{ex.HResult:X8}: {ex.Message})", ex);
        }
    }
}
