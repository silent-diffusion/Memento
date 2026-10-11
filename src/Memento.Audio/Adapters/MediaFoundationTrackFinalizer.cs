using Memento.Audio.Codecs;
using MixLevels = Memento.Core.Audio.MixLevels;
using Memento.Audio.Mixing;
using Memento.Audio.Writing;
using Memento.Core.Projects;
using Memento.Core.Recording;
using Microsoft.Extensions.Logging;
using CoreHashes = Memento.Core.Audio.FileHashes;

namespace Memento.Audio.Adapters;

/// <summary>
/// The app's <see cref="ITrackFinalizer"/>: always lossless (ARCHITECTURE.md §5). Each track, however many
/// <c>.partN.wav</c> files it spans, is read as one <see cref="WavTrackSet"/> and encoded to <c>tracks/&lt;id&gt;.flac</c>
/// with the Windows FLAC encoder, verified bit-exact against the WAV. The tracks are mixed on the recording timeline
/// with <see cref="TrackMixer"/> into <c>mix.flac</c>, and <c>peaks.json</c> (<c>[[rms, peak], …]</c>) is built from the
/// mix with <see cref="PeakBuilder"/>. A track or mix that cannot be encoded stays as WAV with a specific History note.
/// Capture WAVs are listed as obsolete only once their FLAC verified; the caller deletes them after the manifest is saved.
/// </summary>
public sealed partial class MediaFoundationTrackFinalizer(
    MediaFoundationFlacEncoder flac,
    TimeProvider time,
    ILogger<MediaFoundationTrackFinalizer> logger) : ITrackFinalizer
{
    private const string MixStem = "mix";
    private static readonly FlacEncodeOptions Verified = new() { VerifyBitExact = true, Overwrite = true };

    private readonly ILogger<MediaFoundationTrackFinalizer> _logger = logger;

    public async Task<FinalizedAudio> FinalizeAsync(FinalizeRequest request, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var folder = request.ProjectFolder;
        var notes = new List<string>();
        var warnings = new List<FinalizeWarning>();
        var extraHashes = new Dictionary<string, string>(StringComparer.Ordinal);
        var obsolete = new List<string>();

        var tracks = new List<(FinalizeTrackInput Input, IReadOnlyList<string> Parts, WavTrackSet Set)>();
        foreach (var input in request.Tracks)
        {
            var parts = CaptureParts.Find(folder, input.CaptureFile);
            if (parts.Count == 0)
            {
                notes.Add($"Track {input.TrackId} had no audio file and was left out.");
                continue;
            }

            tracks.Add((input, parts, WavTrackSet.FromParts(parts.Select(p => Full(folder, p)))));
        }

        if (tracks.Count == 0)
        {
            throw new InvalidDataException("None of the recording's tracks has an audio file on disk.");
        }

        var steps = tracks.Count + 3;
        var done = 0;
        void Step() => progress?.Report((int)(100L * ++done / steps));

        // 1. Mix on the recording timeline, from the capture WAVs, to a temporary WAV set. With "Even out source levels"
        // each track's speech level is measured first and a quiet track is lifted in the mix (ENGINE-NOTES.md §Q).
        RemoveMixLeftovers(folder);
        var gains = request.EvenLevels && tracks.Count > 1
            ? await TrackLevels.EvenAsync(tracks.Select(t => (t.Input.TrackId, (Func<DecodedAudio>)(() => MediaFoundationDecoder.Open(t.Set)))).ToList(), cancellationToken).ConfigureAwait(false)
            : null;
        var inputs = tracks.Select((t, i) =>
        {
            var start = TimeSpan.FromMilliseconds(t.Input.StartOffsetMs);
            TimeSpan? end = t.Input.EndedAtMs is { } e ? TimeSpan.FromMilliseconds(Math.Max(e, t.Input.StartOffsetMs)) : null;
            return MixInput.FromTrack(t.Set, start, end, gains is null ? 1f : MixLevels.Linear(gains[i].GainDb));
        }).ToList();
        var mix = await TrackMixer.MixAsync(inputs, folder, MixStem, MixOptions.Default, null, cancellationToken).ConfigureAwait(false);
        if (mix.LimitedSamples > 0)
        {
            LogLimited(mix.LimitedSamples, mix.PeakBeforeLimiter);
        }

        var mixSet = WavTrackSet.FromParts(mix.Parts);
        Step();

        // 2. Peaks for the waveform, from the lossless mix.
        var peaks = await PeakBuilder.BuildAsync(mixSet, cancellationToken).ConfigureAwait(false);
        await PeakBuilder.WriteAsync(peaks, Path.Combine(folder, ProjectLayout.PeaksFile), cancellationToken).ConfigureAwait(false);
        Step();

        // 3. Every track to FLAC, verified bit-exact; a failure keeps that track's WAV.
        var finalized = new List<FinalizedTrack>();
        foreach (var (input, parts, set) in tracks)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var relative = Path.ChangeExtension(parts[0], ".flac").Replace('\\', '/');
            var destination = Full(folder, relative);
            var durationMs = (long)set.Duration.TotalMilliseconds;
            try
            {
                ClearReadOnly(destination);
                var result = await flac.EncodeAsync(set, destination, Verified, null, cancellationToken).ConfigureAwait(false);
                MarkReadOnly(destination);
                finalized.Add(new FinalizedTrack(input.TrackId, relative, "flac", set.Format.SampleRate, set.Format.Channels, durationMs, result.Bytes, result.Sha256));
                obsolete.AddRange(parts);
            }
            catch (AudioEncodeException ex)
            {
                LogKeptWav(ex, input.TrackId);
                warnings.Add(new FinalizeWarning($"Kept {input.TrackId} as WAV", ex.Message));
                finalized.Add(await KeepWavAsync(folder, input.TrackId, parts, set, extraHashes, cancellationToken).ConfigureAwait(false));
            }

            Step();
        }

        // 4. The mix to FLAC; its WAV is only an intermediate and goes once the FLAC verified.
        FinalizedFile mixFile;
        var mixDuration = (long)mixSet.Duration.TotalMilliseconds;
        var mixPath = Full(folder, MixStem + ".flac");
        try
        {
            ClearReadOnly(mixPath);
            var result = await flac.EncodeAsync(mixSet, mixPath, Verified, null, cancellationToken).ConfigureAwait(false);
            MarkReadOnly(mixPath);
            mixFile = new FinalizedFile(MixStem + ".flac", "flac", mixSet.Format.SampleRate, mixSet.Format.Channels, mixDuration, result.Bytes, result.Sha256);
            RemoveMixLeftovers(folder);
        }
        catch (AudioEncodeException ex)
        {
            LogKeptWav(ex, MixStem);
            warnings.Add(new FinalizeWarning("Kept the mix as WAV", ex.Message));
            var mixParts = mix.Parts.Select(p => Path.GetRelativePath(folder, p).Replace('\\', '/')).ToList();
            var kept = await KeepWavAsync(folder, MixStem, mixParts, mixSet, extraHashes, cancellationToken).ConfigureAwait(false);
            mixFile = new FinalizedFile(kept.File, "wav", kept.SampleRate, kept.Channels, kept.DurationMs, kept.SizeBytes, kept.Sha256);
        }

        Step();
        var codec = finalized.All(t => t.Codec == "flac") ? "flac" : "flac and wav";
        LogFinalized(folder, finalized.Count, codec, mixDuration);
        return new FinalizedAudio(finalized, mixFile, ProjectLayout.PeaksFile, codec, time.GetLocalNow(), notes, obsolete)
        {
            Warnings = warnings,
            ExtraHashes = extraHashes,
            MixGains = gains,
        };
    }

    /// <summary>
    /// "Rebuild the mix" (after 2.0.0-beta.1): the stored tracks, in any format Media Foundation decodes, are mixed again
    /// on the recording timeline (with evened levels when asked) into <c>&lt;stem&gt;.flac</c>, verified bit-exact
    /// against the mixed WAV and hashed, with <c>&lt;stem&gt;.peaks.json</c>. The current mix and the tracks are never
    /// touched; a failure or a cancel leaves nothing behind.
    /// </summary>
    public async Task<RebuiltMix> RebuildMixAsync(MixRebuildRequest request, IProgress<int>? progress, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var folder = request.ProjectFolder;
        var tracks = request.Tracks.Select(t => (Track: t, Path: Full(folder, t.File))).ToList();
        if (tracks.FirstOrDefault(t => !File.Exists(t.Path)) is { Track: { } missing })
        {
            throw new FileNotFoundException($"The file of track {missing.TrackId} ({missing.File}) is missing, so the mix cannot be built again.");
        }

        var wavStem = request.Stem;
        var flacRelative = request.Stem + ".flac";
        var peaksRelative = request.Stem + ".peaks.json";
        RemoveLeftovers(folder, wavStem, flacRelative, peaksRelative);
        try
        {
            var gains = request.EvenLevels
                ? await TrackLevels.EvenAsync(tracks.Select(t => (t.Track.TrackId, (Func<DecodedAudio>)(() => MediaFoundationDecoder.Open(t.Path)))).ToList(), cancellationToken).ConfigureAwait(false)
                : null;
            progress?.Report(20);
            var inputs = tracks.Select((t, i) =>
            {
                var start = TimeSpan.FromMilliseconds(t.Track.StartOffsetMs);
                TimeSpan? end = t.Track.EndedAtMs is { } e ? TimeSpan.FromMilliseconds(Math.Max(e, t.Track.StartOffsetMs)) : null;
                return MixInput.FromFile(t.Path, start, end, gains is null ? 1f : MixLevels.Linear(gains[i].GainDb));
            }).ToList();
            var mix = await TrackMixer.MixAsync(inputs, folder, wavStem, MixOptions.Default, null, cancellationToken).ConfigureAwait(false);
            var mixSet = WavTrackSet.FromParts(mix.Parts);
            progress?.Report(55);
            var peaks = await PeakBuilder.BuildAsync(mixSet, cancellationToken).ConfigureAwait(false);
            await PeakBuilder.WriteAsync(peaks, Full(folder, peaksRelative), cancellationToken).ConfigureAwait(false);
            progress?.Report(65);
            var result = await flac.EncodeAsync(mixSet, Full(folder, flacRelative), Verified, null, cancellationToken).ConfigureAwait(false);
            progress?.Report(100);
            var file = new FinalizedFile(flacRelative, "flac", mixSet.Format.SampleRate, mixSet.Format.Channels, (long)mixSet.Duration.TotalMilliseconds, result.Bytes, result.Sha256);
            RemoveLeftovers(folder, wavStem);
            return new RebuiltMix(file, peaksRelative, gains);
        }
        catch
        {
            RemoveLeftovers(folder, wavStem, flacRelative, peaksRelative);
            throw;
        }
    }

    /// <summary>The rebuild's intermediate WAV parts, and the given files (relative paths).</summary>
    private static void RemoveLeftovers(string folder, string wavStem, params string[] files)
    {
        foreach (var path in WavTrackSet.FindParts(folder, wavStem).Concat(WavTrackSet.FindParts(folder, wavStem + ".partial")).Concat(files.Select(f => Full(folder, f))))
        {
            if (File.Exists(path))
            {
                ClearReadOnly(path);
                File.Delete(path);
            }
        }
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

    /// <summary>The intermediate mix WAVs (and any left by an interrupted run). They are rebuilt from the tracks.</summary>
    private static void RemoveMixLeftovers(string folder)
    {
        var leftovers = WavTrackSet.FindParts(folder, MixStem)
            .Concat(WavTrackSet.FindParts(folder, MixStem + ".partial"));
        foreach (var path in leftovers)
        {
            ClearReadOnly(path);
            File.Delete(path);
        }
    }

    private static async Task<FinalizedTrack> KeepWavAsync(
        string folder,
        string trackId,
        IReadOnlyList<string> parts,
        WavTrackSet set,
        Dictionary<string, string> extraHashes,
        CancellationToken cancellationToken)
    {
        string? first = null;
        long size = 0;
        foreach (var part in parts)
        {
            var path = Full(folder, part);
            var sha256 = await CoreHashes.Sha256Async(path, cancellationToken).ConfigureAwait(false);
            size += new FileInfo(path).Length;
            MarkReadOnly(path);
            if (first is null)
            {
                first = sha256;
            }
            else
            {
                extraHashes[part] = sha256;
            }
        }

        return new FinalizedTrack(trackId, parts[0], "wav", set.Format.SampleRate, set.Format.Channels, (long)set.Duration.TotalMilliseconds, size, first!);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "{Track} could not be encoded to FLAC; its WAV is kept")]
    private partial void LogKeptWav(Exception exception, string track);

    [LoggerMessage(Level = LogLevel.Information, Message = "Mix limiter bent {Samples} samples (peak before limiting {Peak})")]
    private partial void LogLimited(long samples, float peak);

    [LoggerMessage(Level = LogLevel.Information, Message = "Finalized {Folder}: {Tracks} tracks as {Codec}, mix {DurationMs} ms")]
    private partial void LogFinalized(string folder, int tracks, string codec, long durationMs);
}
