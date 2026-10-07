using System.Diagnostics;
using Memento.Core.Workers;
using Memento.Transcription;
using SherpaOnnx;

namespace Memento.Worker;

/// <summary>
/// The speaker job (ENGINE-NOTES.md §E): sherpa-onnx offline diarization per track with pyannote segmentation 3.0 and
/// the chosen embedding model, fast clustering (threshold or a fixed count), confidence on, CPU. Each finished track is
/// sent at once (<c>diarized</c>) with its speakers' voice embeddings.
/// </summary>
internal sealed class SherpaDiarizer(ProtocolWriter output)
{
    /// <summary>At most this much of a speaker's speech goes into their voice embedding.</summary>
    private const int VoiceSeconds = 60;

    private const double MinTurnSeconds = 0.5;

    public DiarizeResult Run(DiarizeJob job, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        foreach (var path in new[] { job.SegmentationModelPath, job.EmbeddingModelPath })
        {
            if (!File.Exists(path))
            {
                throw new WorkerFailure(WorkerErrorCodes.ModelLoad, $"the model file {Path.GetFileName(path)} is missing");
            }
        }

        var config = new OfflineSpeakerDiarizationConfig();
        config.Segmentation.Pyannote.Model = job.SegmentationModelPath;
        config.Segmentation.NumThreads = job.Threads;
        config.Segmentation.Provider = "cpu";
        config.Embedding.Model = job.EmbeddingModelPath;
        config.Embedding.NumThreads = job.Threads;
        config.Embedding.Provider = "cpu";
        config.Clustering.NumClusters = job.NumClusters > 0 ? job.NumClusters : -1;
        config.Clustering.Threshold = job.Threshold;
        config.Clustering.ComputeConfidence = 1;
        config.MinDurationOn = 0.3f;
        config.MinDurationOff = 0.5f;

        OfflineSpeakerDiarization diarization;
        try
        {
            diarization = new OfflineSpeakerDiarization(config);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new WorkerFailure(WorkerErrorCodes.ModelLoad, ex.Message, ex);
        }

        using (diarization)
        {
            var results = new List<DiarizedTrack>();
            double audioSeconds = 0;
            for (var t = 0; t < job.Tracks.Count; t++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var track = job.Tracks[t];
                var samples = TrackAudio.ReadAll(track.Path, cancellationToken);
                audioSeconds += samples.Length / (double)TrackAudio.SampleRate;
                var index = t;
                var segments = diarization.ProcessWithCallback(
                    samples,
                    (processed, total, _) =>
                    {
                        var share = total <= 0 ? 1.0 : processed / (double)total;
                        output.Send(new WorkerReply { Type = WorkerMessageTypes.Progress, Percent = Math.Round(100.0 * (index + share) / job.Tracks.Count, 1), TrackId = track.Id });
                        return 0;
                    },
                    IntPtr.Zero);
                cancellationToken.ThrowIfCancellationRequested();
                var voices = Voices(job.EmbeddingModelPath, job.Threads, samples, segments, cancellationToken);
                var finished = new DiarizedTrack(
                    track.Id,
                    segments.Select(s => new SpeakerTurn(Math.Round(s.Start + track.OffsetSeconds, 3), Math.Round(s.End + track.OffsetSeconds, 3), s.Speaker, Math.Round(s.Confidence, 4))).ToList(),
                    voices,
                    Math.Round(samples.Length / (double)TrackAudio.SampleRate, 3));
                results.Add(finished);

                // Sent as soon as the track is done, so a job stopped later keeps it (speakers.partial.json).
                output.Send(new WorkerReply { Type = WorkerMessageTypes.Diarized, TrackId = track.Id, Diarized = finished });
            }

            return new DiarizeResult(results, audioSeconds, stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Each speaker's voice embedding from up to <see cref="VoiceSeconds"/> of their turns (turns shorter than half a
    /// second are skipped: too little to hear a voice in), so the host can tell the same person on another track.
    /// </summary>
    private static List<SpeakerVoice> Voices(string embeddingModelPath, int threads, float[] samples, OfflineSpeakerDiarizationSegment[] segments, CancellationToken cancellationToken)
    {
        var voices = new List<SpeakerVoice>();
        if (segments.Length == 0)
        {
            return voices;
        }

        var config = new SpeakerEmbeddingExtractorConfig { Model = embeddingModelPath, NumThreads = threads, Provider = "cpu" };
        using var extractor = new SpeakerEmbeddingExtractor(config);
        foreach (var speaker in segments.GroupBy(s => s.Speaker).OrderBy(g => g.Key))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var audio = new List<float>();
            foreach (var turn in speaker.Where(t => t.End - t.Start >= MinTurnSeconds).OrderByDescending(t => t.End - t.Start))
            {
                var from = Math.Clamp((int)(turn.Start * TrackAudio.SampleRate), 0, samples.Length);
                var to = Math.Clamp((int)(turn.End * TrackAudio.SampleRate), from, samples.Length);
                var room = (VoiceSeconds * TrackAudio.SampleRate) - audio.Count;
                audio.AddRange(samples.AsSpan(from, Math.Min(to - from, room)));
                if (audio.Count >= VoiceSeconds * TrackAudio.SampleRate)
                {
                    break;
                }
            }

            if (audio.Count < MinTurnSeconds * TrackAudio.SampleRate)
            {
                continue;
            }

            using var stream = extractor.CreateStream();
            stream.AcceptWaveform(TrackAudio.SampleRate, [.. audio]);
            stream.InputFinished();
            if (extractor.IsReady(stream))
            {
                voices.Add(new SpeakerVoice(speaker.Key, extractor.Compute(stream).Select(v => (float)Math.Round(v, 5)).ToArray(), Math.Round(audio.Count / (double)TrackAudio.SampleRate, 2)));
            }
        }

        return voices;
    }
}
