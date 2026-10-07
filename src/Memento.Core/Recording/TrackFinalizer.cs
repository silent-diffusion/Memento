using System.Globalization;
using Memento.Core.Audio;
using Memento.Core.Projects;
using Microsoft.Extensions.Logging;

namespace Memento.Core.Recording;

/// <summary>
/// Default <see cref="ITrackFinalizer"/>: mixdown and peaks with <see cref="PcmMixer"/>, encoding with the
/// registered <see cref="IAudioEncoder"/> for the configured codec (falling back to WAV when none is installed),
/// SHA-256 of every result. Finished tracks and the mix are marked read-only.
/// </summary>
public sealed partial class TrackFinalizer(
    IEnumerable<IAudioEncoder> encoders,
    TimeProvider time,
    ILogger<TrackFinalizer> logger) : ITrackFinalizer
{
    private readonly IReadOnlyList<IAudioEncoder> _encoders = encoders.ToList();
    private readonly ILogger<TrackFinalizer> _logger = logger;

    public async Task<FinalizedAudio> FinalizeAsync(FinalizeRequest request, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var folder = request.ProjectFolder;
        var notes = new List<string>();
        var encoder = PickEncoder(request.Storage.Codec, notes);
        var options = new AudioEncodeOptions(encoder.IsLossless ? null : request.Storage.BitrateKbps, request.Storage.DownmixMono);
        var steps = request.Tracks.Count + 2;
        var done = 0;
        void Step() => progress?.Report((int)(100L * ++done / steps));

        // 1. Mix and peaks, from the capture WAVs, in one streaming pass.
        var inputs = new List<MixInput>();
        foreach (var track in request.Tracks)
        {
            var path = Full(folder, track.CaptureFile);
            if (File.Exists(path))
            {
                inputs.Add(new MixInput(path, track.StartOffsetMs));
            }
            else
            {
                notes.Add($"Track {track.TrackId} had no audio file and was left out of the mix.");
            }
        }

        var mixTemporary = Path.Combine(folder, ProjectLayout.MixBaseName + ".tmp.wav");
        var mix = await Task.Run(
            () => PcmMixer.Mix(inputs, mixTemporary, request.Storage.DownmixMono, PeakBuilder.DefaultWindowMs, cancellationToken),
            cancellationToken);
        await mix.Peaks.WriteAsync(Path.Combine(folder, ProjectLayout.PeaksFile), cancellationToken);
        if (mix.ClippedSamples > 0)
        {
            LogLimited(mix.ClippedSamples);
        }

        var mixRelative = ProjectLayout.MixBaseName + encoder.FileExtension;
        var mixPath = Full(folder, mixRelative);
        ClearReadOnly(mixPath);
        if (encoder.Codec == PassThroughWavEncoder.WavCodec)
        {
            File.Move(mixTemporary, mixPath, overwrite: true);
        }
        else
        {
            await encoder.EncodeAsync(mixTemporary, mixPath, options with { DownmixMono = false }, cancellationToken);
            File.Delete(mixTemporary);
        }

        Step();

        // 2. Tracks.
        var tracks = new List<FinalizedTrack>();
        var obsolete = new List<string>();
        foreach (var track in request.Tracks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var capturePath = Full(folder, track.CaptureFile);
            if (!File.Exists(capturePath))
            {
                Step();
                continue;
            }

            var captureInfo = WavInfo.Read(capturePath);
            var durationMs = captureInfo.Format.BytesToMilliseconds(
                Math.Min(captureInfo.DeclaredDataBytes, new FileInfo(capturePath).Length - captureInfo.DataOffset));
            var keepCapture = encoder.Codec == PassThroughWavEncoder.WavCodec && (!options.DownmixMono || captureInfo.Format.Channels == 1);
            var finalRelative = keepCapture
                ? track.CaptureFile
                : Path.ChangeExtension(track.CaptureFile, encoder.FileExtension).Replace('\\', '/');
            var finalPath = Full(folder, finalRelative);
            if (!keepCapture)
            {
                ClearReadOnly(finalPath);
                await encoder.EncodeAsync(capturePath, finalPath, options, cancellationToken);
                if (!string.Equals(finalPath, capturePath, StringComparison.OrdinalIgnoreCase))
                {
                    obsolete.Add(track.CaptureFile);
                }
            }

            var channels = options.DownmixMono ? 1 : captureInfo.Format.Channels;
            var size = new FileInfo(finalPath).Length;
            var hash = await FileHashes.Sha256Async(finalPath, cancellationToken);
            MarkReadOnly(finalPath);
            tracks.Add(new FinalizedTrack(
                track.TrackId,
                finalRelative,
                encoder.Codec,
                captureInfo.Format.SampleRate,
                channels,
                durationMs,
                size,
                hash));
            Step();
        }

        var mixHash = await FileHashes.Sha256Async(mixPath, cancellationToken);
        var mixSize = new FileInfo(mixPath).Length;
        MarkReadOnly(mixPath);
        Step();

        LogFinalized(folder, tracks.Count, encoder.Codec, mix.DurationMs);
        return new FinalizedAudio(
            tracks,
            new FinalizedFile(mixRelative, encoder.Codec, mix.Format.SampleRate, mix.Format.Channels, mix.DurationMs, mixSize, mixHash),
            ProjectLayout.PeaksFile,
            encoder.Codec,
            time.GetLocalNow(),
            notes,
            obsolete);
    }

    private IAudioEncoder PickEncoder(string codec, List<string> notes)
    {
        var match = _encoders.FirstOrDefault(e => string.Equals(e.Codec, codec, StringComparison.Ordinal));
        if (match is not null)
        {
            return match;
        }

        var fallback = _encoders.FirstOrDefault(e => e.Codec == PassThroughWavEncoder.WavCodec) ?? new PassThroughWavEncoder();
        if (codec != PassThroughWavEncoder.WavCodec)
        {
            notes.Add(string.Create(CultureInfo.InvariantCulture, $"Kept as WAV: this build has no {codec.ToUpperInvariant()} encoder. Nothing was lost; the files are larger."));
            LogEncoderMissing(codec);
        }

        return fallback;
    }

    /// <summary>A file named by the manifest, never outside the project folder (<see cref="ProjectPaths"/>).</summary>
    private static string Full(string folder, string relative) => ProjectPaths.Resolve(folder, relative);

    private static void MarkReadOnly(string path) =>
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);

    private static void ClearReadOnly(string path)
    {
        if (File.Exists(path))
        {
            File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "No encoder for storage codec {Codec} is installed; tracks are kept as WAV")]
    private partial void LogEncoderMissing(string codec);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mix limiter bent {Samples} samples")]
    private partial void LogLimited(long samples);

    [LoggerMessage(Level = LogLevel.Information, Message = "Finalized {Folder}: {Tracks} tracks as {Codec}, mix {DurationMs} ms")]
    private partial void LogFinalized(string folder, int tracks, string codec, long durationMs);
}
