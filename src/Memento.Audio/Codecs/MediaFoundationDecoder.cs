using Memento.Audio.Mixing;
using Memento.Audio.Writing;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace Memento.Audio.Codecs;

/// <summary>
/// Decodes any supported file (WAV, FLAC, MP3, M4A/AAC) to float32 frames for peaks, mixdown and transcription.
/// WAV files we wrote (and multi-part tracks) are read directly, everything else through Media Foundation.
/// </summary>
public static class MediaFoundationDecoder
{
    /// <summary>Sample rate the transcription and diarization engines take.</summary>
    public const int TranscriptionSampleRate = 16_000;

    public static DecodedAudio Open(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (string.Equals(Path.GetExtension(path), ".wav", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                return Open(WavTrackSet.FromParts([path]));
            }
            catch (InvalidDataException)
            {
                // A WAV variant we do not parse (ADPCM, odd chunks): let Media Foundation try.
            }
        }

        MediaFoundationRuntime.EnsureStarted();
        var reader = new MediaFoundationReader(path, new MediaFoundationReader.MediaFoundationReaderSettings { RequestFloatOutput = true });
        try
        {
            return new DecodedAudio(reader.ToSampleProvider(), reader, reader.TotalTime);
        }
        catch
        {
            reader.Dispose();
            throw;
        }
    }

    /// <summary>Opens all parts of a recorded track as one stream (no Media Foundation needed).</summary>
    public static DecodedAudio Open(WavTrackSet track)
    {
        ArgumentNullException.ThrowIfNull(track);
        var reader = track.OpenReader();
        return new DecodedAudio(reader.ToSampleProvider(), reader, track.Duration);
    }

    /// <summary>Opens <paramref name="path"/> as 16 kHz mono for transcription.</summary>
    public static DecodedAudio OpenForTranscription(string path) => ToTranscriptionFormat(Open(path));

    /// <summary>Downmixes to mono and resamples to 16 kHz with NAudio's WDL resampler. Takes ownership of <paramref name="audio"/>.</summary>
    public static DecodedAudio ToTranscriptionFormat(DecodedAudio audio)
    {
        ArgumentNullException.ThrowIfNull(audio);
        ISampleProvider mono = audio.Channels == 1 ? audio : new ChannelMapSampleProvider(audio, 1);
        ISampleProvider resampled = mono.WaveFormat.SampleRate == TranscriptionSampleRate ? mono : new WdlResamplingSampleProvider(mono, TranscriptionSampleRate);
        return new DecodedAudio(resampled, audio, audio.Duration);
    }

    /// <summary>Resamples to <paramref name="sampleRate"/> (WDL) if needed. Takes ownership of <paramref name="audio"/>.</summary>
    public static DecodedAudio Resample(DecodedAudio audio, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(audio);
        return audio.SampleRate == sampleRate ? audio : new DecodedAudio(new WdlResamplingSampleProvider(audio, sampleRate), audio, audio.Duration);
    }
}
