using System.Diagnostics;
using Memento.Core.Workers;
using Memento.Transcription;
using SherpaOnnx;

namespace Memento.Worker;

/// <summary>
/// The speaker job (ENGINE-NOTES.md §E): sherpa-onnx offline diarization per track with pyannote segmentation 3.0 and
/// the chosen embedding model, fast clustering (threshold or a fixed count), confidence on, CPU.
/// </summary>
internal sealed class SherpaDiarizer(ProtocolWriter output)
{
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
                results.Add(new DiarizedTrack(
                    track.Id,
                    segments.Select(s => new SpeakerTurn(Math.Round(s.Start + track.OffsetSeconds, 3), Math.Round(s.End + track.OffsetSeconds, 3), s.Speaker, Math.Round(s.Confidence, 4))).ToList()));
            }

            return new DiarizeResult(results, audioSeconds, stopwatch.ElapsedMilliseconds);
        }
    }
}
