using Memento.Audio.Codecs;
using Memento.Audio.Writing;
using Memento.Core.Audio;
using Memento.Core.Settings;

namespace Memento.Audio.Adapters;

/// <summary>
/// The Windows Media Foundation encoders as Core <see cref="IAudioEncoder"/>s: <c>flac</c> (from a WAV, verified
/// bit-exact, always every channel), and <c>aac</c> (<c>.m4a</c>) and <c>mp3</c> from any file Windows decodes (the
/// <c>optimize</c> stage passes the stored FLAC), with the bitrate and optional mono downmix. Failures surface as
/// <see cref="IOException"/> with the encoder's specific message, as the interface promises.
/// </summary>
public sealed class MediaFoundationAudioEncoder : IAudioEncoder
{
    private readonly MediaFoundationFlacEncoder? _flac;
    private readonly MediaFoundationLossyEncoder? _lossy;
    private readonly LossyCodec _lossyCodec;

    private MediaFoundationAudioEncoder(string codec, string extension, MediaFoundationFlacEncoder? flac, MediaFoundationLossyEncoder? lossy, LossyCodec lossyCodec)
    {
        Codec = codec;
        FileExtension = extension;
        _flac = flac;
        _lossy = lossy;
        _lossyCodec = lossyCodec;
    }

    public string Codec { get; }

    public string FileExtension { get; }

    public bool IsLossless => _flac is not null;

    public static MediaFoundationAudioEncoder Flac(MediaFoundationFlacEncoder encoder) => new(StorageSettings.Flac, ".flac", encoder, null, default);

    public static MediaFoundationAudioEncoder Aac(MediaFoundationLossyEncoder encoder) => new(StorageSettings.Aac, ".m4a", null, encoder, LossyCodec.Aac);

    public static MediaFoundationAudioEncoder Mp3(MediaFoundationLossyEncoder encoder) => new(StorageSettings.Mp3, ".mp3", null, encoder, LossyCodec.Mp3);

    public async Task EncodeAsync(string sourceWavPath, string destinationPath, AudioEncodeOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        try
        {
            if (_flac is not null)
            {
                var set = WavTrackSet.FromParts([sourceWavPath]);
                await _flac.EncodeAsync(set, destinationPath, new FlacEncodeOptions { VerifyBitExact = true, Overwrite = true }, null, cancellationToken).ConfigureAwait(false);
                return;
            }

            var bitrate = options.BitrateKbps ?? StorageSettings.DefaultLossyBitrateKbps;
            var lossy = new LossyEncodeOptions(_lossyCodec, bitrate) { DownmixToMono = options.DownmixMono, Overwrite = true };
            await _lossy!.EncodeAsync(sourceWavPath, destinationPath, lossy, null, cancellationToken).ConfigureAwait(false);
        }
        catch (AudioEncodeException ex)
        {
            var hresult = ex.InnerException is IOException io && DiskErrors.IsDiskFull(io) ? io.HResult : ex.HResult;
            throw new IOException(ex.Message, ex) { HResult = hresult };
        }
    }
}
